/** One myTax deduction section in a frozen tax pack. */
export interface MyTaxSection { section: string; paperReference: string; claimType: string | null; approvedTotal: number; entryAmount: number; claims: number; categories: string[]; }

/** A pack's myTax sections; packs frozen before multi-section support held only "Other work-related expenses" (D5 E). */
export function sectionsOf(assessment: { sections?: MyTaxSection[] | null; approvedTotal: number; entryAmount: number; rows: { status: string }[] }): MyTaxSection[] {
  return assessment.sections ?? [{ section: 'Other work-related expenses', paperReference: 'D5 E', claimType: null, approvedTotal: assessment.approvedTotal,
    entryAmount: assessment.entryAmount, claims: assessment.rows.filter((row) => row.status === 'Approved').length, categories: [] }];
}

/** Entry progress for a section; older packs have a single unnamed row. */
export function progressFor(progress: { section: string; status: string }[], section: string) {
  return (progress.find((item) => item.section === section) ?? (progress.length === 1 ? progress[0] : undefined))?.status ?? 'NotStarted';
}
