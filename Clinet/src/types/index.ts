// User roles and permissions
export type UserRole = 'super_admin' | 'admin' | 'user';

export interface User {
  id: string;
  name: string;
  role: UserRole;
}

// Provider status
export type ProviderStatus = 'active' | 'inactive' | 'pending';

export interface Provider {
  id: string;
  name: string;
  nameHe: string;
  status: ProviderStatus;
  lastChecked: Date;
}

// Contact from CSV – כל השדות כפי שהם בקובץ, בלי נרמול
export interface Contact {
  id: string;
  phone: string; // כפי שהופיע בקובץ
  isValid: boolean;
  validationError?: string;
  [key: string]: string | boolean | undefined; // Dynamic columns
}

// Campaign
export type CampaignStatus = 'draft' | 'running' | 'paused' | 'completed' | 'stopped';

export interface Campaign {
  id: string;
  name: string;
  message: string;
  messageLanguage: 'he' | 'en' | 'ar';
  contacts: Contact[];
  status: CampaignStatus;
  progress: number;
  totalSent: number;
  totalFailed: number;
  createdAt: Date;
  updatedAt: Date;
}

// SMS Test
export interface SMSTest {
  id: string;
  phone: string;
  message: string;
  status: 'pending' | 'sent' | 'failed';
  sentAt: Date;
}

// CSV Import
export interface CSVColumn {
  key: string;
  label: string;
  isPhoneColumn: boolean;
}

export interface CSVImportResult {
  columns: CSVColumn[];
  data: Contact[];
  validCount: number;
  invalidCount: number;
}

// Role permissions
export const ROLE_PERMISSIONS: Record<UserRole, {
  canManageUsers: boolean;
  canManageProviders: boolean;
  canCreateCampaign: boolean;
  canDeleteCampaign: boolean;
  canSendSMS: boolean;
  canViewReports: boolean;
}> = {
  super_admin: {
    canManageUsers: true,
    canManageProviders: true,
    canCreateCampaign: true,
    canDeleteCampaign: true,
    canSendSMS: true,
    canViewReports: true,
  },
  admin: {
    canManageUsers: false,
    canManageProviders: true,
    canCreateCampaign: true,
    canDeleteCampaign: true,
    canSendSMS: true,
    canViewReports: true,
  },
  user: {
    canManageUsers: false,
    canManageProviders: false,
    canCreateCampaign: true,
    canDeleteCampaign: false,
    canSendSMS: true,
    canViewReports: false,
  },
};

// Hebrew labels
export const ROLE_LABELS: Record<UserRole, string> = {
  super_admin: 'מנהל ראשי',
  admin: 'מנהל',
  user: 'משתמש',
};

export const STATUS_LABELS: Record<ProviderStatus, string> = {
  active: 'פעיל',
  inactive: 'לא פעיל',
  pending: 'ממתין',
};

export const CAMPAIGN_STATUS_LABELS: Record<CampaignStatus, string> = {
  draft: 'טיוטה',
  running: 'פעיל',
  paused: 'מושהה',
  completed: 'הושלם',
  stopped: 'הופסק',
};
