import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import type { User, UserRole, Provider, Campaign, Contact, SMSTest, CampaignStatus } from '@/types';

interface AppState {
  // Auth
  isAuthenticated: boolean;
  setAuth: (authenticated: boolean, user?: User) => void;
  logout: () => void;

  // User
  currentUser: User;
  setUserRole: (role: UserRole) => void;

  // Selected Provider
  selectedProviderId: string;
  setSelectedProviderId: (id: string) => void;

  // Providers
  providers: Provider[];
  updateProviderStatus: (id: string, status: Provider['status']) => void;

  // Campaigns
  campaigns: Campaign[];
  activeCampaign: Campaign | null;
  addCampaign: (
    campaign: Omit<Campaign, 'id' | 'createdAt' | 'updatedAt'> & { preferredId?: string }
  ) => Campaign;
  updateCampaign: (id: string, updates: Partial<Campaign>) => void;
  deleteCampaign: (id: string) => void;
  setActiveCampaign: (campaign: Campaign | null) => void;
  updateCampaignProgress: (id: string, progress: number, sent: number, failed: number) => void;
  stopCampaign: (id: string) => void;

  // Contacts (current import) – תמיד מוחלף במלואו בהעלאת קובץ חדש
  importedContacts: Contact[];
  importedColumns: string[];
  importedPhoneColumnName: string | null;
  setImportedData: (contacts: Contact[], columns: string[], phoneColumnName: string) => void;
  clearImportedData: () => void;

  // SMS Tests
  smsTests: SMSTest[];
  addSMSTest: (test: Omit<SMSTest, 'id' | 'sentAt'>) => void;

  // UI State
  isCampaignRunning: boolean;
  setIsCampaignRunning: (running: boolean) => void;

  // Campaign Form State (shared between CampaignManager and StartCampaignButton)
  campaignFormName: string;
  setCampaignFormName: (name: string) => void;
  campaignFormMessage: string;
  setCampaignFormMessage: (message: string) => void;
  campaignFormSenderIdMode: 'random' | 'specific' | 'alphanumeric';
  setCampaignFormSenderIdMode: (mode: 'random' | 'specific' | 'alphanumeric') => void;
  campaignFormCustomSenderId: string;
  setCampaignFormCustomSenderId: (id: string) => void;
  campaignFormLanguage: 'he' | 'en' | 'ar';
  setCampaignFormLanguage: (lang: 'he' | 'en' | 'ar') => void;

  // Clear all campaign-form data + imported contacts + active campaign.
  // Used when navigating back to the main screen so a campaign can't be resent by mistake.
  resetCampaignForm: () => void;
}

const generateId = () => Math.random().toString(36).substring(2, 11);

export const useAppStore = create<AppState>()(
  persist(
    (set, get) => ({
      // Auth
      isAuthenticated: false,
      setAuth: (authenticated, user) =>
        set({
          isAuthenticated: authenticated,
          currentUser: user || { id: '1', name: 'משתמש מערכת', role: 'super_admin' },
        }),
      logout: () =>
        set({ isAuthenticated: false }),

      // Initial user
      currentUser: {
        id: '1',
        name: 'משתמש מערכת',
        role: 'super_admin',
      },

      setUserRole: (role) =>
        set((state) => ({
          currentUser: { ...state.currentUser, role },
        })),

      // Selected Provider
      selectedProviderId: '2', // Partner by default
      setSelectedProviderId: (id) => set({ selectedProviderId: id }),

      // Initial providers
      providers: [
        {
          id: '1',
          name: 'Cellcom',
          nameHe: 'סלקום',
          status: 'active',
          lastChecked: new Date(),
        },
        {
          id: '2',
          name: 'Partner',
          nameHe: 'פרטנר',
          status: 'active',
          lastChecked: new Date(),
        },
      ],

      updateProviderStatus: (id, status) =>
        set((state) => ({
          providers: state.providers.map((p) =>
            p.id === id ? { ...p, status, lastChecked: new Date() } : p
          ),
        })),

      // Campaigns
      campaigns: [],
      activeCampaign: null,

      addCampaign: (campaignData) => {
        const { preferredId, ...rest } = campaignData;
        const newCampaign: Campaign = {
          ...rest,
          id: preferredId ?? generateId(),
          createdAt: new Date(),
          updatedAt: new Date(),
        };
        set((state) => ({
          campaigns: [newCampaign, ...state.campaigns],
        }));
        return newCampaign;
      },

      updateCampaign: (id, updates) =>
        set((state) => ({
          campaigns: state.campaigns.map((c) =>
            c.id === id ? { ...c, ...updates, updatedAt: new Date() } : c
          ),
        })),

      deleteCampaign: (id) =>
        set((state) => ({
          campaigns: state.campaigns.filter((c) => c.id !== id),
          activeCampaign: state.activeCampaign?.id === id ? null : state.activeCampaign,
        })),

      setActiveCampaign: (campaign) => set({ activeCampaign: campaign }),

      updateCampaignProgress: (id, progress, sent, failed) =>
        set((state) => ({
          campaigns: state.campaigns.map((c) =>
            c.id === id
              ? {
                  ...c,
                  progress,
                  totalSent: sent,
                  totalFailed: failed,
                  status: progress >= 100 ? 'completed' : c.status,
                  updatedAt: new Date(),
                }
              : c
          ),
          activeCampaign:
            state.activeCampaign?.id === id
              ? {
                  ...state.activeCampaign,
                  progress,
                  totalSent: sent,
                  totalFailed: failed,
                  status: progress >= 100 ? 'completed' : state.activeCampaign.status,
                }
              : state.activeCampaign,
        })),

      stopCampaign: (id) =>
        set((state) => ({
          campaigns: state.campaigns.map((c) =>
            c.id === id
              ? { ...c, status: 'stopped' as CampaignStatus, updatedAt: new Date() }
              : c
          ),
          activeCampaign:
            state.activeCampaign?.id === id
              ? { ...state.activeCampaign, status: 'stopped' as CampaignStatus }
              : state.activeCampaign,
          isCampaignRunning: false,
        })),

      // Contacts – החלפה מלאה (לא מיזוג) בכל העלאה
      importedContacts: [],
      importedColumns: [],
      importedPhoneColumnName: null,

      setImportedData: (contacts, columns, phoneColumnName) =>
        set({
          importedContacts: [...contacts],
          importedColumns: [...columns],
          importedPhoneColumnName: phoneColumnName,
        }),

      clearImportedData: () =>
        set({
          importedContacts: [],
          importedColumns: [],
          importedPhoneColumnName: null,
        }),

      // SMS Tests
      smsTests: [],

      addSMSTest: (test) =>
        set((state) => ({
          smsTests: [
            {
              ...test,
              id: generateId(),
              sentAt: new Date(),
            },
            ...state.smsTests,
          ],
        })),

      // UI State
      isCampaignRunning: false,
      setIsCampaignRunning: (running) => set({ isCampaignRunning: running }),

      // Campaign Form State
      campaignFormName: '',
      setCampaignFormName: (name) => set({ campaignFormName: name }),
      campaignFormMessage: '',
      setCampaignFormMessage: (message) => set({ campaignFormMessage: message }),
      campaignFormSenderIdMode: 'random',
      setCampaignFormSenderIdMode: (mode) => set({ campaignFormSenderIdMode: mode }),
      campaignFormCustomSenderId: '',
      setCampaignFormCustomSenderId: (id) => set({ campaignFormCustomSenderId: id }),
      campaignFormLanguage: 'he',
      setCampaignFormLanguage: (lang) => set({ campaignFormLanguage: lang }),

      resetCampaignForm: () =>
        set({
          campaignFormName: '',
          campaignFormMessage: '',
          campaignFormSenderIdMode: 'random',
          campaignFormCustomSenderId: '',
          campaignFormLanguage: 'he',
          importedContacts: [],
          importedColumns: [],
          importedPhoneColumnName: null,
          activeCampaign: null,
        }),
    }),
    {
      name: 'sms-campaign-storage',
      partialize: (state) => ({
        isAuthenticated: state.isAuthenticated,
        currentUser: state.currentUser,
        providers: state.providers,
        campaigns: state.campaigns,
        smsTests: state.smsTests,
        selectedProviderId: state.selectedProviderId,
        // Persist active campaign so refreshing the progress page keeps the user there.
        activeCampaign: state.activeCampaign,
      }),
    }
  )
);
