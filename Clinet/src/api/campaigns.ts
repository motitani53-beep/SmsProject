import axios from 'axios';
import type { Contact } from '@/types';
import { API_BASE_URL } from '@/config/api';

/** Web API expects full language names (aligned with Postman / CampaignRequestDto). */
export function toApiMessageLanguage(uiLang: string): string {
  switch (uiLang) {
    case 'he':
      return 'hebrew';
    case 'en':
      return 'english';
    case 'ar':
      return 'arabic';
    default:
      return uiLang;
  }
}

export type CampaignRecipientPayload = {
  phone_number: string;
  custom_fields: Record<string, string>;
};

/** Body shape for POST /api/Campaign (matches WebApplication1 CampaignRequestDto). */
export type CreateCampaignPayload = {
  campaign_name: string;
  message_content: string;
  message_language: string;
  recipients: CampaignRecipientPayload[];
  provider: string;
  sender_config: {
    sender_type: string;
    sender_value?: string | null;
  };
  scheduling: {
    type: string;
    scheduled_time?: string | null;
  };
  priority?: string;
  code?: string | null;
};

export type CreateCampaignResponse = {
  campaignId: number;
  status: string;
  message: string;
  recipientsCount: number;
};

function customFieldsForContact(
  contact: Contact,
  importedPhoneColumnName: string | null
): Record<string, string> {
  if (contact.custom_fields && Object.keys(contact.custom_fields).length > 0) {
    return { ...contact.custom_fields };
  }
  const skip = new Set<string>(['id', 'phone', 'isValid', 'validationError', 'custom_fields']);
  if (importedPhoneColumnName) skip.add(importedPhoneColumnName);
  const out: Record<string, string> = {};
  for (const key of Object.keys(contact)) {
    if (skip.has(key)) continue;
    const v = contact[key];
    if (typeof v === 'string') out[key] = v;
  }
  return out;
}

/** One recipient: phone_number from contact.phone; custom_fields from contact.custom_fields (or flat CSV fields). */
export function toRecipientPayload(
  contact: Contact,
  importedPhoneColumnName: string | null
): CampaignRecipientPayload {
  return {
    phone_number: contact.phone,
    custom_fields: customFieldsForContact(contact, importedPhoneColumnName),
  };
}

export function buildCreateCampaignPayload(params: {
  campaign_name: string;
  message_content: string;
  message_language: string;
  contacts: Contact[];
  provider: string;
  senderMode: 'random' | 'specific' | 'alphanumeric';
  senderValue: string;
  importedPhoneColumnName: string | null;
  scheduledTime?: Date | null;
}): CreateCampaignPayload {
  const recipients = params.contacts
    .filter((c) => c.isValid)
    .map((c) => toRecipientPayload(c, params.importedPhoneColumnName));

  let sender_type: string;
  let sender_value: string | null | undefined;
  if (params.senderMode === 'random') {
    sender_type = 'random';
    sender_value = null;
  } else if (params.senderMode === 'specific') {
    // API accepts "specific" (UI) or "manual_number" (legacy); backend normalizes to manual_number for storage.
    sender_type = 'specific';
    sender_value = params.senderValue.trim() || null;
  } else {
    sender_type = 'manual_string';
    sender_value = params.senderValue.trim() || null;
  }

  return {
    campaign_name: params.campaign_name,
    message_content: params.message_content,
    message_language: toApiMessageLanguage(params.message_language),
    recipients,
    provider: params.provider,
    sender_config: {
      sender_type,
      sender_value,
    },
    scheduling: params.scheduledTime
      ? {
          type: 'scheduled',
          scheduled_time: params.scheduledTime.toISOString(),
        }
      : {
          type: 'immediate',
        },
    priority: 'low',
  };
}

export async function postCreateCampaign(
  payload: CreateCampaignPayload
): Promise<CreateCampaignResponse> {
  const { data } = await axios.post<CreateCampaignResponse>(
    `${API_BASE_URL}/api/Campaign`,
    payload,
    { headers: { 'Content-Type': 'application/json' } }
  );
  return data;
}

/** Matches GET /api/Campaign/{id} JSON (camelCase from ASP.NET). */
export type CampaignDeliveryDetailDto = {
  id: number;
  campaignId: number;
  phoneNumber: string;
  messageContent: string;
  status: number;
  additionalData?: Record<string, unknown> | null;
};

export type CampaignDetailResponse = {
  id: number;
  campaignName: string;
  messageContent: string;
  totalMessages: number;
  /** e.g. Scheduled | Processing | In Progress — UI treats Processing like In Progress for users. */
  status: string;
  scheduledTime?: string | null;
  deliveryDetails: CampaignDeliveryDetailDto[];
};

export async function getCampaignById(id: number): Promise<CampaignDetailResponse> {
  const { data } = await axios.get<CampaignDetailResponse>(`${API_BASE_URL}/api/Campaign/${id}`);
  return data;
}

export async function deleteCampaign(id: number): Promise<void> {
  await axios.delete(`${API_BASE_URL}/api/Campaign/${id}`);
}
