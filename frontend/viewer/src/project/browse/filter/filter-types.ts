import type {WritingSystemSelection} from '$project/data';

export type Op = 'starts-with' | 'contains' | 'ends-with' | 'equals' | 'not-equals';

export type SelectedField = {
  id: string;
  ws: WritingSystemSelection;
};

export type MissingOption = {
  id: string;
  label: string;
};
