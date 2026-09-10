import {type IWritingSystem, WritingSystemType} from '$lib/dotnet-types';
import type {WritingSystemService} from '$project/data';
import type {Task} from './tasks-service';

export function writingSystemOf(writingSystems: WritingSystemService, task: Task): IWritingSystem | undefined {
  if (!task.subjectWritingSystemId) return undefined;
  const candidates = task.subjectWritingSystemType === WritingSystemType.Vernacular
    ? writingSystems.vernacular
    : writingSystems.analysis;
  return candidates.find(ws => ws.wsId === task.subjectWritingSystemId);
}

export function wsColorClass(writingSystems: WritingSystemService, ws: IWritingSystem): string {
  return writingSystems.wsColor(ws.wsId, ws.type === WritingSystemType.Vernacular ? 'vernacular' : 'analysis');
}
