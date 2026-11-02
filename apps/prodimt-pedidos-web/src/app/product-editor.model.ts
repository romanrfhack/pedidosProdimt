import { UnitCode } from './operational-labels';

export interface ProductEditorValue {
  id: string | null;
  name: string;
  description: string | null;
  unitCode: UnitCode | null;
}
