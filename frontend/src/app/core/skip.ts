/** Why a receipt is skipped (left out of the tax return). Matches the server's list. */
export const skipReasons: { key: string; label: string }[] = [
  { key: 'Personal', label: 'Personal' },
  { key: 'NotDeductible', label: 'Not deductible' },
  { key: 'Duplicate', label: 'Duplicate' },
  { key: 'OtherYear', label: 'Other year' },
  { key: 'Other', label: 'Other' },
];
export const skipReasonLabel = (key: string | null | undefined) => skipReasons.find((r) => r.key === key)?.label ?? null;
