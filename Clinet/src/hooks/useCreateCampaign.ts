import { useMutation } from '@tanstack/react-query';
import { postCreateCampaign, type CreateCampaignPayload } from '@/api/campaigns';

export function useCreateCampaign() {
  return useMutation({
    mutationFn: (payload: CreateCampaignPayload) => postCreateCampaign(payload),
  });
}
