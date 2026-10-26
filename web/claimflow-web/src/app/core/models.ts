export type ClaimStatus = 'Received' | 'InAdjudication' | 'Pended' | 'Approved' | 'Denied' | 'Paid';
export type ClaimLineStatus = 'Pending' | 'Approved' | 'Denied';
export type ClaimSource = 'Api' | 'Portal' | 'X12Edi';
export type FindingOutcome = 'Pass' | 'Pend' | 'Deny';
export type Role = 'Submitter' | 'Adjudicator' | 'Admin';

export const CLAIM_STATUSES: ClaimStatus[] = ['Received', 'InAdjudication', 'Pended', 'Approved', 'Denied', 'Paid'];

export interface LoginResponse {
  token: string;
  expiresAt: string;
  email: string;
  displayName: string;
  role: Role;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface ClaimListItem {
  id: string;
  claimNumber: string;
  memberNumber: string;
  memberName: string;
  providerName: string;
  serviceDateFrom: string;
  status: ClaimStatus;
  source: ClaimSource;
  totalBilled: number;
  planPaid: number;
  submittedAt: string;
}

export interface ClaimLine {
  lineNumber: number;
  procedureCode: string;
  modifier: string | null;
  serviceDate: string;
  units: number;
  billedAmount: number;
  status: ClaimLineStatus;
  allowedAmount: number;
  deductibleAmount: number;
  copayAmount: number;
  coinsuranceAmount: number;
  planPaid: number;
  denialCode: string | null;
  denialDescription: string | null;
}

export interface Finding {
  ruleName: string;
  outcome: FindingOutcome;
  reasonCode: string | null;
  message: string;
  lineNumber: number | null;
}

export interface StatusChange {
  from: ClaimStatus | null;
  to: ClaimStatus;
  changedAt: string;
  actor: string;
  note: string | null;
}

export interface ProviderDto {
  id: string;
  npi: string;
  name: string;
  specialty: string;
  isInNetwork: boolean;
  isActive: boolean;
}

export interface ClaimDetail {
  id: string;
  claimNumber: string;
  patientControlNumber: string | null;
  status: ClaimStatus;
  source: ClaimSource;
  pendReason: string | null;
  submittedAt: string;
  submittedBy: string;
  adjudicatedAt: string | null;
  paidAt: string | null;
  paymentReference: string | null;
  member: { id: string; memberNumber: string; fullName: string; planName: string };
  provider: ProviderDto;
  diagnosisCodes: string[];
  totalBilled: number;
  totalAllowed: number;
  planPaid: number;
  memberResponsibility: number;
  lines: ClaimLine[];
  findings: Finding[];
  history: StatusChange[];
}

export interface AuditEntry {
  timestamp: string;
  action: string;
  actor: string;
  details: string | null;
}

export interface MemberDto {
  id: string;
  memberNumber: string;
  fullName: string;
  dateOfBirth: string;
  planCode: string;
  planName: string;
  coverageStart: string;
  coverageEnd: string | null;
  deductibleMet: number;
  annualDeductible: number;
  outOfPocketMet: number;
  outOfPocketMax: number;
}

export interface FeeScheduleDto {
  procedureCode: string;
  description: string;
  allowedAmount: number;
  requiresPriorAuthorization: boolean;
  isOfficeVisit: boolean;
}

export interface DashboardSummary {
  totalClaims: number;
  totalBilled: number;
  totalPaid: number;
  autoAdjudicationRate: number;
  averageDaysToAdjudicate: number;
  byStatus: { status: ClaimStatus; count: number; billed: number }[];
  topDenialReasons: { reasonCode: string; description: string; count: number }[];
  dailyVolume: { date: string; received: number; finalized: number }[];
}

export interface SubmitClaimRequest {
  memberNumber: string;
  providerNpi: string;
  patientControlNumber: string | null;
  diagnosisCodes: string[];
  lines: { procedureCode: string; modifier: string | null; serviceDate: string; units: number; billedAmount: number }[];
}

export interface ClaimSubmitted {
  id: string;
  claimNumber: string;
  status: ClaimStatus;
}

export interface X12BatchResult {
  interchangeControlNumber: string | null;
  claimsInFile: number;
  accepted: ClaimSubmitted[];
  rejected: { index: number; patientControlNumber: string | null; errors: string[] }[];
  fileErrors: string[];
}

export interface PaymentRun {
  runReference: string;
  claimsPaid: number;
  totalPaid: number;
  claimNumbers: string[];
}

export interface ProblemDetails {
  title?: string;
  detail?: string;
  status?: number;
  errors?: Record<string, string[]>;
}
