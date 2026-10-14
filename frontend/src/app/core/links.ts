// Official links shown in the app. Each was opened and checked on 11 October 2026; recheck when ATO restructures its site.
const ato = 'https://www.ato.gov.au/individuals-and-families';
export const links = {
  myGov: 'https://my.gov.au/',
  lodgeWithMyTax: `${ato}/your-tax-return/how-to-lodge-your-tax-return/lodge-your-tax-return-online-with-mytax`,
  preparingReturn: `${ato}/your-tax-return/before-you-prepare-your-tax-return/preparing-your-tax-return`,
  deductionsYouCanClaim: `${ato}/income-deductions-offsets-and-records/deductions-you-can-claim`,
  unionFees: `${ato}/income-deductions-offsets-and-records/deductions-you-can-claim/memberships-accreditations-fees-and-commissions/union-fees-subscriptions-to-associations-and-bargaining-agents-fees`,
  recordsToKeep: `${ato}/income-deductions-offsets-and-records/records-you-need-to-keep`,
  myDeductions: 'https://www.ato.gov.au/online-services/online-services-for-individuals-and-sole-traders/ato-app/using-mydeductions/mydeductions',
  taxAgentRegister: 'https://www.tpb.gov.au/public-register',
};

/** myTax instructions for "Other work-related expenses" (D5). Only verified years get a direct link. */
export function myTaxOtherWorkExpenses(financialYearStart: number) {
  const returnYear = financialYearStart + 1;
  return returnYear === 2025 || returnYear === 2026
    ? `${ato}/your-tax-return/instructions-to-complete-your-tax-return/mytax-instructions/${returnYear}/deductions/work-related-expenses/other-work-related-expenses`
    : links.lodgeWithMyTax;
}
