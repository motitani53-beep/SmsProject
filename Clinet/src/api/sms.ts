import axios from 'axios';
import { API_BASE_URL } from '@/config/api';

export type SendTestRequest = {
  phone_number: string;
  message_content: string;
  custom_fields?: Record<string, string>;
  message_language?: 'hebrew' | 'english' | 'arabic' | string;
  sender_value?: string | null;
};

export type SendTestResponse = {
  testMessageId: number;
  phoneNumber: string;
  messageContent: string;
  status: string;
  createdAt: string;
};

/** POST /api/sms/send-test — returns 200 OK with the persisted TestMessage shape on success. */
export async function postSendTest(payload: SendTestRequest): Promise<SendTestResponse> {
  const { data } = await axios.post<SendTestResponse>(`${API_BASE_URL}/api/sms/send-test`, payload, {
    headers: { 'Content-Type': 'application/json' },
  });
  return data;
}
