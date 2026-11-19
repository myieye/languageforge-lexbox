---
name: quarterly-analytics
description: Collect the quarterly product analytics for FieldWorks Lite and Lexbox and enter them in the FieldWorks Lite and Lexbox rows of the LangTech Analytics Dashboard Google Sheet. Gathers GitHub releases, forum topics, Mixpanel users/MAU/countries/installs and support tickets (from the support Google Group) itself, reads the project counts from a Lexbox admin endpoint through the user's browser session, asks only about FTE and anomalies, then writes the sheet after a final OK.
when_to_use: User says "quarterly analytics", "collect the analytics", "fill the analytics sheet", "LangTech analytics", "/quarterly-analytics FY27Q1", or a scheduled quarterly reminder kicks it off.
argument-hint: "[FYyyQn, default: the last finished quarter]"
disable-model-invocation: true
allowed-tools: Bash(python .claude/skills/quarterly-analytics/scripts/public_sources.py:*) Read Glob Grep
---

# Quarterly product analytics: FieldWorks Lite + Lexbox

Fills the **FieldWorks Lite** and **Lexbox** rows of one quarter's tab (e.g. `FY27Q1`) in the Google Sheet "LangTech Analytics Dashboard". The shared Google Doc "Product Analytics (Lexbox & FW-Lite)" describes the same methods for humans; keep it in step when a method here changes.

SIL fiscal years start Oct 1 and are named after the calendar year they end in: FY27Q1 = Oct-Dec 2026, Q2 = Jan-Mar, Q3 = Apr-Jun, Q4 = Jul-Sep. The last finished quarter on, say, 2026-10-14 is FY26Q4.

## Ground rules

- **Privacy.** The metrics endpoint returns counts only, the script prints counts plus public release tags and forum topic titles, and neither writes to disk. Support-thread subjects and anything else user-identifying stay in this conversation: don't save them to files, commit them or post them anywhere (repo AGENTS.md privacy rules).
- **No cluster access.** Everything from prod comes through the admin metrics endpoint on lexbox.org, read as the user; never kubectl, psql or port-forwards.
- **Ask little, with AskUserQuestion, lightest question first.** Ask about FTE every quarter and anything the standing rules below don't settle (anomalies, flags). Don't ask the user to read files.
- **Nothing goes into the sheet before the user's final OK.** Write only the two product rows, never another product's cells.

## Steps

1. **Quarter and preflight.** Use the argument, or the last finished quarter. Check which connectors are attached: Google Drive + Sheets (required), Mixpanel. Support tickets need the built-in browser pane or Claude in Chrome; without either, see the Google Group section.
   - **No Mixpanel connector:** ask whether to browse Mixpanel with Claude in Chrome, or use Mixpanel's Query API with a service account. For the API, the user sets `MIXPANEL_SA_USER` and `MIXPANEL_SA_SECRET` in their environment. Hand them to curl on stdin, never on its command line, where other processes can read them: `printf 'user = "%s:%s"\n' "$MIXPANEL_SA_USER" "$MIXPANEL_SA_SECRET" | curl -K - ...` (`printf` is a shell builtin). Print only aggregate results, and never echo, log or inspect those variables.
2. **Read the sheet.** Find "LangTech Analytics Dashboard" with Drive search. Read rows 1-2 (headers) and the FieldWorks Lite and Lexbox rows of this quarter's tab, plus the previous quarter's tab for comparison and the FTE history. Map columns by header text, because the columns change between quarters. If the quarter's tab doesn't exist yet, stop and say so: the sheet owner creates it.
3. **Collect, in parallel where you can:**
   - `python .claude/skills/quarterly-analytics/scripts/public_sources.py <quarter>` covers releases, forum topics and the FTE sanity check (about 20 s).
   - Project counts from the Lexbox admin endpoint: `https://lexbox.org/api/metrics/projects/<start>/<end>`, with `start` the quarter's first day and `end` the first day of the next quarter (exclusive), e.g. `https://lexbox.org/api/metrics/projects/2026-10-01/2027-01-01` for FY27Q1. It needs a logged-in Lexbox admin. If the built-in browser pane or Claude in Chrome is available, open the URL there (the user is logged in) and read the JSON; otherwise give the user the URL and ask them to paste the JSON. The response is `{"fwLite": {"activeProjects", "newProjects", "languagesImpacted"}, "lexbox": {"createdProjects"}}`. It scans every CRDT commit, so expect a few seconds and don't call it in a loop. Dates are UTC. The FW Lite numbers go by each commit's client-assigned HybridDateTime (the server keeps no receipt time), so a device with a wrong clock can move a project between quarters.
   - Mixpanel and support tickets, as described below.
4. **Check.** Compare every value with last quarter. Flag changes over about 50%, missing values, and any `check:` lines from `public_sources.py`.
5. **Ask.** FTE first (see the rules), then any anomalies. Then show the final two rows, the sheet's URL and tab, and ask for the OK to write them and sign off as the Owner listed on the "Product List" tab.
6. **Write and verify.** Write with the Sheets connector (`update_values`, numbers as numbers, `null` for cells to leave alone), then read the rows back.
7. **Offer the doc update.** Offer to update the doc's per-quarter bullets ("FY27Q1: ...") as suggestions.

## Standing rules (agreed 2026-10-14; ask before changing one)

| Column | FieldWorks Lite | Lexbox |
|---|---|---|
| Latest Release Version / Date, Number of Releases | Releases published in the quarter that have FW Lite assets (`public_sources.py`) | Releases published in the quarter whose notes have Lexbox items |
| Supported FTE, Paid FTE | Ask every quarter, pre-filled from last quarter's tab. It's the team total split across the two rows, never the total on both. History is in earlier tabs and the doc's "Supported FTE" section. Show the PR-label share from `public_sources.py` as a sanity check. | Same question |
| Downloads | Not collected from FY27Q1 on (dropped 2026-10-14, though the sheet config still marks it required). Leave blank. | n/a |
| Installs | Mixpanel: users whose first-ever `app_launched` falls in the quarter. See the FY27Q1 caveat. | n/a |
| Active Projects | Endpoint `fwLite.activeProjects`: projects with a CRDT commit in the quarter, deleted ones included | Mixpanel: distinct `project_id` on `send_receive` with `direction` = `send` in the quarter, i.e. projects someone pushed to. See "Lexbox project counts" under Mixpanel. |
| New Projects Started | Endpoint `fwLite.newProjects`: first-ever CRDT commit in the quarter | Endpoint `lexbox.createdProjects`: projects created in the quarter, deleted ones included |
| Active Users | Mixpanel unique users of `app_launched` in the quarter | Mixpanel unique users of any `lexbox` event in the quarter |
| Avg MAU, MAU month 1-3 | Mixpanel unique users per calendar month; Avg = mean, 1 decimal | Same |
| Number of User Countries | Mixpanel distinct `mp_country_code` over the quarter | Same (Lexbox events carry the client IP) |
| Number of Languages Impacted | Endpoint `fwLite.languagesImpacted`: active projects whose retention policy isn't test, dev or training (unknown and verified count). No manual judgment. | Lexbox Active Projects minus the projects whose `retention_policy` is `test`, `dev` or `training` (same recipe) |
| New Support Tickets | Threads *started* in the quarter in the `lexbox_support` group, sorted into FW Lite or Lexbox by content. Count borderline support requests. Exclude FW Lite beta join requests, alerts (prod alerts, Honeycomb triggers), spam, FYI forwards and messages with no request in them. | Same |
| New Community Topics | Topics created in the quarter in the FW Lite forum category (`public_sources.py`) | 0, there is no forum |
| Analytics contain test projects/users (Product List tab) | Yes | Yes |

## Mixpanel

Everything is in the Mixpanel project named "Lexbox" (`Get-Projects` gives its id):
- **FW Lite:** `app_launched`, once per app start (desktop and Android). Tracking started with v2026-09-01. Don't filter it on `product`: that property only arrived around v2026-09-28, and older installs still send `app_launched` without it.
- **Lexbox:** server-side `send_receive`, `fw_lite_sync`, `login_completed` and `account_created`, keyed by user id, all with `product` = `lexbox`. Query `$all_events` filtered on `product` = `lexbox`. Tracking started 2026-09-28, so FY27Q1 is its first full quarter and there's nothing earlier to compare with.

Queries go through the connector's `Run-Query` (`insights`):
- **Quarter totals** (Active Users, countries): `chartType` `bar` over the quarter's absolute date range, which counts each user once. Countries are the `aggregate-property` measurement, `unique_values` of `mp_country_code`.
- **MAU:** `chartType` `line` with `unit` `month`. Never add months up.
- **Installs:** the query schema has no "first time ever" filter, so subtract: unique users from 2026-01-01 to the quarter's last day, minus unique users from 2026-01-01 to the day before the quarter.
- To split by event, break down on `$event_name` ("Event Name" returns `undefined`).

**Lexbox project counts.** Run `insights` on `send_receive` filtered on `direction` = `send` over the quarter's absolute date range, measuring `unique_values` of `project_id` (the `aggregate-property` measurement), broken down on `retention_policy` with one custom group of values `test`, `dev` and `training`. Active Projects is the breakdown's `total`, which Mixpanel counts once per project (never add up buckets: a project whose policy changed in the quarter sits in more than one). Languages Impacted is that total minus the custom group, which also counts each project once, so a project moved out of those policies mid-quarter is left out; policies rarely change, so that's noise. Subtract buckets rather than filtering on `retention_policy` "does not equal": events sent before the property shipped (October 2026) lack it, and those count as real projects. It counts the push date, not the commit date, and only sees users who haven't opted out. FwHeadless pushes through the same proxy under its own Lexbox account, so projects edited only in FW Lite count too once FwHeadless syncs them to FLEx, and that account counts as one Lexbox user.

Use only `Run-Query` and the Get/List tools; never create, edit, delete, merge or dismiss anything in Mixpanel.

Users who opted out of analytics never show up, and the team's own devices do.

**FY27Q1 caveat (delete this paragraph once FY27Q2 is done).** Installs overcount in FY27Q1. Existing devices that first sent `app_launched` after updating to an analytics build (Sep 2026 or later) look like new installs. The effect fades in FY27Q2 and is negligible after that. In FY27Q1 add a short note to the FW Lite Notes cell; in FY27Q2 add one only if Installs look high against Active Users.

## Support tickets via the Google Group

Read the `lexbox_support@groups.sil.org` Google Group in the browser. Never use a Gmail connector, even if one is attached: it can read the user's whole mailbox, and only holds the group mail that user happened to receive. The built-in browser pane keeps its own sign-ins, so if the group shows a Google sign-in or no permission, ask the user to sign in there with an account that can see the group, or switch to Claude in Chrome. If neither works, ask the user to count the quarter's new threads themselves, using the rules below.

The conversation list is sorted by last activity, so page back until the threads were last active before the quarter, and count only threads whose first message falls in the quarter. Read dates in UTC, and double-check threads that start within a day of a quarter boundary. Classify from the list (subject, sender, snippet); everything read goes to Anthropic, so open a thread only when the list doesn't settle its start date, its product or whether it's a request. When in doubt, open it: the count matters more. Skip alerts (FIRING, RESOLVED, Triggered), spam and marketing mail.

The group splits conversations by subject, so:
- **A follow-up under a new subject** from the same person about the same request counts once.
- **A conversation whose first message is a reply** ("Re: …") continues an earlier one; date it by that original.

Only read; never post, reply or change anything in the group.
