/** Display names and chart colours for the expense categories the tax rules understand. */
export const categories: Record<string, { label: string; color: string }> = {
  SelfEducation: { label: 'Self-education', color: 'var(--purple)' },
  SoftwareSubscriptions: { label: 'Software and subscriptions', color: 'var(--sky)' },
  ToolsEquipment: { label: 'Tools and equipment', color: 'var(--blue-ink)' },
  PhoneInternet: { label: 'Phone and internet', color: 'var(--purple-dark)' },
  WorkClothing: { label: 'Work clothing', color: 'var(--yellow)' },
  UnionFees: { label: 'Union fees', color: 'var(--purple-ink)' },
  ProfessionalMembership: { label: 'Professional memberships', color: 'var(--mint-ink)' },
  Donations: { label: 'Gifts and donations', color: 'var(--orange-ink)' },
  TaxAffairs: { label: 'Managing tax affairs', color: 'var(--ink-soft)' },
  Unsupported: { label: 'Needs advice', color: 'var(--orange)' },
  Excluded: { label: 'Excluded', color: 'var(--mint)' },
};
export const categoryLabel = (key: string) => categories[key]?.label ?? key;
/** Claim type codes on the paper return, in plain words. */
export const claimTypes: Record<string, string> = {
  K: 'K · keeps up or improves skills for my current job',
  I: 'I · likely to increase income from my current job',
  C: 'C · compulsory uniform',
  N: 'N · non-compulsory registered uniform',
  S: 'S · occupation-specific clothing',
  P: 'P · protective clothing',
};
