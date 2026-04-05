import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAppStore } from '@/store/appStore';
import { Play, Loader2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { toast } from 'sonner';
import { findMissingPlaceholders } from '@/utils/messagePlaceholders';

export function StartCampaignButton() {
  const {
    importedContacts,
    importedColumns,
    providers,
    addCampaign,
    setActiveCampaign,
    campaignFormName,
    campaignFormMessage,
    campaignFormLanguage,
    campaignFormSenderIdMode,
    campaignFormCustomSenderId,
  } = useAppStore();

  const navigate = useNavigate();
  const [launching, setLaunching] = useState(false);

  const validContacts = importedContacts.filter((c) => c.isValid);
  const allProvidersActive = providers.every((p) => p.status === 'active');
  const hasInvalidContacts = importedContacts.length > 0 && importedContacts.some((c) => !c.isValid);
  const missingPlaceholders =
    importedColumns.length > 0
      ? findMissingPlaceholders(campaignFormMessage, importedColumns)
      : [];

  const isDisabled =
    launching ||
    !campaignFormName.trim() ||
    !campaignFormMessage.trim() ||
    importedContacts.length === 0 ||
    hasInvalidContacts ||
    missingPlaceholders.length > 0 ||
    !allProvidersActive ||
    (campaignFormSenderIdMode !== 'random' && !campaignFormCustomSenderId.trim());

  const handleStartCampaign = async () => {
    if (isDisabled) return;

    setLaunching(true);

    const toastId = toast.loading('שולח קמפיין...', { position: 'top-center' });

    try {
      // Simulate API call
      await new Promise((resolve) => setTimeout(resolve, 2000));

      // Simulate success (90% success rate for demo)
      const success = Math.random() > 0.1;
      if (!success) throw new Error('API error');

      const finalSenderId = campaignFormSenderIdMode === 'random' ? 'Random' : campaignFormCustomSenderId;

      const campaign = addCampaign({
        name: campaignFormName,
        message: campaignFormMessage,
        messageLanguage: campaignFormLanguage,
        senderId: finalSenderId,
        contacts: validContacts,
        status: 'running',
        progress: 0,
        totalSent: 0,
        totalFailed: 0,
      });

      setActiveCampaign(campaign);

      toast.success('הקמפיין נשלח בהצלחה!', { id: toastId, position: 'top-center' });

      setTimeout(() => {
        navigate('/campaign-progress');
      }, 1500);
    } catch {
      toast.error('שגיאת מערכת', { id: toastId, position: 'top-center' });
      setLaunching(false);
    }
  };

  return (
    <div className="widget-card">
      <Button
        onClick={handleStartCampaign}
        disabled={isDisabled}
        className="w-full btn-gradient"
        size="lg"
      >
        {launching ? (
          <>
            <Loader2 className="h-5 w-5 ml-2 animate-spin" />
            שולח...
          </>
        ) : (
          <>
            <Play className="h-5 w-5 ml-2" />
            התחל קמפיין
          </>
        )}
      </Button>
    </div>
  );
}
