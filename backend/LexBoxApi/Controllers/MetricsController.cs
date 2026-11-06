using LexBoxApi.Auth.Attributes;
using LexCore.Entities;
using LexData;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SIL.Harmony.Core;

namespace LexBoxApi.Controllers;

[ApiController]
[Route("/api/metrics")]
[AdminRequired]
public class MetricsController(LexBoxDbContext dbContext) : ControllerBase
{
    public record ProjectMetrics(FwLiteProjectMetrics FwLite, LexboxProjectMetrics Lexbox);
    public record FwLiteProjectMetrics(int ActiveProjects, int NewProjects, int LanguagesImpacted);
    public record LexboxProjectMetrics(int CreatedProjects);

    /// <summary>Project counts for [start, end), dates in UTC. Deleted projects count like any other.</summary>
    // Dates go in the path because the browser pane the skill reads this through hides page output when the URL has a query string.
    [HttpGet("projects/{start}/{end}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ProjectMetrics>> Projects(DateOnly start, DateOnly end, CancellationToken token)
    {
        if (start >= end) return BadRequest("start must be before end");
        // CrdtCommits is only indexed on ProjectId, so the range pass reads the whole table. One such pass, then an indexed
        // NOT EXISTS per active project, beats grouping every commit ever made.
        dbContext.Database.SetCommandTimeout(TimeSpan.FromMinutes(2));
        var startUtc = new DateTimeOffset(start, TimeOnly.MinValue, TimeSpan.Zero);
        var endUtc = new DateTimeOffset(end, TimeOnly.MinValue, TimeSpan.Zero);
        var commits = dbContext.Set<ServerCommit>();

        var activeProjectIds = commits
            .Where(c => c.HybridDateTime.DateTime >= startUtc && c.HybridDateTime.DateTime < endUtc)
            .Select(c => c.ProjectId)
            .Distinct();
        var activeProjects = await dbContext.Projects.IgnoreQueryFilters()
            .Where(p => activeProjectIds.Contains(p.Id))
            .Select(p => new
            {
                p.RetentionPolicy,
                IsNew = !commits.Any(c => c.ProjectId == p.Id && c.HybridDateTime.DateTime < startUtc),
            })
            .ToArrayAsync(token);
        var createdProjects = await dbContext.Projects.IgnoreQueryFilters()
            .CountAsync(p => p.CreatedDate >= startUtc && p.CreatedDate < endUtc, token);

        return new ProjectMetrics(
            new FwLiteProjectMetrics(
                activeProjects.Length,
                activeProjects.Count(p => p.IsNew),
                activeProjects.Count(p => p.RetentionPolicy is not (RetentionPolicy.Test or RetentionPolicy.Dev or RetentionPolicy.Training))),
            new LexboxProjectMetrics(createdProjects));
    }
}
