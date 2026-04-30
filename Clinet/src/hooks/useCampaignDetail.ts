import { useQuery } from '@tanstack/react-query';
import { getCampaignById, type CampaignDetailResponse } from '@/api/campaigns';

export function useCampaignDetail(campaignId: number | null) {
  return useQuery<CampaignDetailResponse>({
    queryKey: ['campaign', campaignId],
    queryFn: () => getCampaignById(campaignId!),
    enabled: campaignId != null && Number.isFinite(campaignId) && campaignId > 0,
    refetchInterval: false,
    refetchOnWindowFocus: false,
  });
}
