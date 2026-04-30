import { useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAppStore } from '@/store/appStore';
import { Play, Loader2, CalendarClock, Calendar as CalendarIcon, Clock } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { Calendar } from '@/components/ui/calendar';
import { Popover, PopoverContent, PopoverTrigger } from '@/components/ui/popover';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { toast } from 'sonner';
import { addDays, format, startOfToday } from 'date-fns';
import { he } from 'date-fns/locale';
import { findMissingPlaceholders, findUnusedColumns } from '@/utils/messagePlaceholders';
import { useCreateCampaign } from '@/hooks/useCreateCampaign';
import { buildCreateCampaignPayload } from '@/api/campaigns';
import { isAxiosError } from 'axios';

const HOURS = Array.from({ length: 24 }, (_, i) => String(i).padStart(2, '0'));
const MINUTES = Array.from({ length: 60 }, (_, i) => String(i).padStart(2, '0'));

const LARGE_TOAST_STYLE: React.CSSProperties = {
  minWidth: '440px',
  minHeight: '90px',
  fontSize: '18px',
  padding: '20px 24px',
  fontWeight: 600,
};

const ERROR_TOAST_STYLE: React.CSSProperties = {
  ...LARGE_TOAST_STYLE,
  color: '#dc2626',
  border: '2px solid #dc2626',
  background: '#fef2f2',
};

const SUCCESS_TOAST_STYLE: React.CSSProperties = {
  ...LARGE_TOAST_STYLE,
  color: '#16a34a',
  border: '2px solid #16a34a',
  background: '#f0fdf4',
};

function getErrorDetail(err: unknown): string {
  if (isAxiosError(err)) {
    if (!err.response) {
      return 'השרת אינו מגיב — ייתכן שהשירות מושבת. נסה שוב מאוחר יותר או פנה לאחראי מערכת.';
    }
    const status = err.response.status;
    const data = err.response.data as { error?: string; details?: string } | undefined;
    if (data?.error) return data.error;
    if (data?.details) return data.details;
    if (typeof err.response.data === 'string' && err.response.data.trim()) {
      return err.response.data;
    }
    if (status >= 500) return `שגיאת שרת (${status}) — נסה שוב מאוחר יותר`;
    if (status === 404) return 'נקודת הקצה לא נמצאה (404)';
    if (status === 401 || status === 403) return 'אין הרשאה לבצע פעולה זו';
    return `הבקשה נכשלה (שגיאה ${status})`;
  }
  return 'אירעה שגיאה לא צפויה';
}

export function StartCampaignButton() {
  const {
    importedContacts,
    importedColumns,
    importedPhoneColumnName,
    providers,
    selectedProviderId,
    addCampaign,
    setActiveCampaign,
    campaignFormName,
    campaignFormMessage,
    campaignFormLanguage,
    campaignFormSenderIdMode,
    campaignFormCustomSenderId,
    resetCampaignForm,
  } = useAppStore();

  const navigate = useNavigate();
  const [launching, setLaunching] = useState(false);
  const [scheduling, setScheduling] = useState(false);
  const [showScheduler, setShowScheduler] = useState(false);
  const [scheduledDate, setScheduledDate] = useState<Date | undefined>(undefined);
  const [scheduledHour, setScheduledHour] = useState<string>('');
  const [scheduledMinute, setScheduledMinute] = useState<string>('');
  const [calendarOpen, setCalendarOpen] = useState(false);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const inFlightRef = useRef(false);
  const createCampaignMutation = useCreateCampaign();

  const validContacts = importedContacts.filter((c) => c.isValid);
  const allProvidersActive = providers.every((p) => p.status === 'active');
  const hasInvalidContacts = importedContacts.length > 0 && importedContacts.some((c) => !c.isValid);
  const missingPlaceholders =
    importedColumns.length > 0
      ? findMissingPlaceholders(campaignFormMessage, importedColumns)
      : [];
  const unusedColumns =
    importedColumns.length > 0
      ? findUnusedColumns(campaignFormMessage, importedColumns, importedPhoneColumnName)
      : [];

  const isDisabled =
    launching ||
    scheduling ||
    createCampaignMutation.isPending ||
    !campaignFormName.trim() ||
    !campaignFormMessage.trim() ||
    importedContacts.length === 0 ||
    hasInvalidContacts ||
    missingPlaceholders.length > 0 ||
    unusedColumns.length > 0 ||
    !allProvidersActive ||
    (campaignFormSenderIdMode !== 'random' && !campaignFormCustomSenderId.trim());

  const handleStartCampaign = async () => {
    if (isDisabled || inFlightRef.current) return;
    inFlightRef.current = true;

    setLaunching(true);

    const toastId = toast.loading('שולח קמפיין...', {
      position: 'top-center',
      style: LARGE_TOAST_STYLE,
    });

    const selectedProvider = providers.find((p) => p.id === selectedProviderId);
    const providerName = selectedProvider?.name?.trim() ?? '';

    const payload = buildCreateCampaignPayload({
      campaign_name: campaignFormName.trim(),
      message_content: campaignFormMessage,
      message_language: campaignFormLanguage,
      contacts: validContacts,
      provider: providerName,
      senderMode: campaignFormSenderIdMode,
      senderValue: campaignFormCustomSenderId,
      importedPhoneColumnName,
    });

    try {
      const result = await createCampaignMutation.mutateAsync(payload);

      const campaign = addCampaign({
        name: campaignFormName,
        message: campaignFormMessage,
        messageLanguage: campaignFormLanguage,
        contacts: validContacts,
        status: 'running',
        progress: 0,
        totalSent: 0,
        totalFailed: 0,
        preferredId: String(result.campaignId),
      });

      setActiveCampaign(campaign);

      toast.success('הקמפיין נשלח בהצלחה!', {
        id: toastId,
        position: 'top-center',
        style: SUCCESS_TOAST_STYLE,
        duration: 700,
      });

      setTimeout(() => {
        toast.dismiss(toastId);
        navigate('/campaign-progress');
      }, 700);
    } catch (err) {
      toast.error('שגיאת מערכת', {
        id: toastId,
        position: 'top-center',
        description: getErrorDetail(err),
        duration: 6000,
        style: ERROR_TOAST_STYLE,
      });
    } finally {
      setLaunching(false);
      inFlightRef.current = false;
    }
  };

  const handleScheduleCampaign = async () => {
    if (isDisabled || inFlightRef.current) return;

    // First click: reveal the datetime picker, don't submit yet.
    if (!showScheduler) {
      setShowScheduler(true);
      return;
    }

    // Combine date + hour + minute into one Date and validate it is in the future.
    let parsed: Date | null = null;
    if (scheduledDate && scheduledHour !== '' && scheduledMinute !== '') {
      parsed = new Date(scheduledDate);
      parsed.setHours(Number(scheduledHour), Number(scheduledMinute), 0, 0);
    }
    const maxScheduledMs = Date.now() + 7 * 24 * 60 * 60 * 1000;
    if (
      !parsed ||
      isNaN(parsed.getTime()) ||
      parsed.getTime() <= Date.now() ||
      parsed.getTime() > maxScheduledMs
    ) {
      toast.error('התזמון שבחרת אינו תקין אנא בחר מועד אחר', {
        position: 'top-center',
        duration: 5000,
        style: ERROR_TOAST_STYLE,
      });
      return;
    }

    inFlightRef.current = true;
    setScheduling(true);

    const toastId = toast.loading('מתזמן קמפיין...', {
      position: 'top-center',
      style: LARGE_TOAST_STYLE,
    });

    const selectedProvider = providers.find((p) => p.id === selectedProviderId);
    const providerName = selectedProvider?.name?.trim() ?? '';

    const payload = buildCreateCampaignPayload({
      campaign_name: campaignFormName.trim(),
      message_content: campaignFormMessage,
      message_language: campaignFormLanguage,
      contacts: validContacts,
      provider: providerName,
      senderMode: campaignFormSenderIdMode,
      senderValue: campaignFormCustomSenderId,
      importedPhoneColumnName,
      scheduledTime: parsed,
    });

    try {
      const result = await createCampaignMutation.mutateAsync(payload);

      const campaign = addCampaign({
        name: campaignFormName,
        message: campaignFormMessage,
        messageLanguage: campaignFormLanguage,
        contacts: validContacts,
        status: 'draft',
        progress: 0,
        totalSent: 0,
        totalFailed: 0,
        preferredId: String(result.campaignId),
      });

      setActiveCampaign(campaign);

      toast.success('קמפיין תוזמן בהצלחה', {
        id: toastId,
        position: 'top-center',
        style: SUCCESS_TOAST_STYLE,
        duration: 700,
      });

      setShowScheduler(false);
      setScheduledDate(undefined);
      setScheduledHour('');
      setScheduledMinute('');

      setTimeout(() => {
        toast.dismiss(toastId);
        navigate('/campaign-progress');
      }, 700);
    } catch (err) {
      toast.error('שגיאת מערכת', {
        id: toastId,
        position: 'top-center',
        description: getErrorDetail(err),
        duration: 6000,
        style: ERROR_TOAST_STYLE,
      });
    } finally {
      setScheduling(false);
      inFlightRef.current = false;
    }
  };

  return (
    <div className="widget-card space-y-3">
      <Button
        onClick={() => {
          if (isDisabled) return;
          setConfirmOpen(true);
        }}
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

      {showScheduler && (
        <div className="space-y-3 p-4 bg-muted/40 border border-border rounded-lg animate-in fade-in slide-in-from-top-1 duration-200">
          <p className="text-sm font-semibold text-foreground text-center">מועד לתזמון הקמפיין</p>

          <div className="space-y-1.5">
            <Label className="flex items-center gap-1.5 text-xs text-muted-foreground">
              <CalendarIcon className="h-3.5 w-3.5" />
              תאריך
            </Label>
            <Popover open={calendarOpen} onOpenChange={setCalendarOpen}>
              <PopoverTrigger asChild>
                <Button
                  type="button"
                  variant="outline"
                  className="w-full justify-start text-right font-normal"
                >
                  <CalendarIcon className="h-4 w-4 ml-2" />
                  {scheduledDate
                    ? format(scheduledDate, 'EEEE, d בMMMM yyyy', { locale: he })
                    : 'בחר תאריך'}
                </Button>
              </PopoverTrigger>
              <PopoverContent className="w-auto p-0" align="start">
                <Calendar
                  mode="single"
                  selected={scheduledDate}
                  onSelect={(d) => {
                    setScheduledDate(d);
                    setCalendarOpen(false);
                  }}
                  disabled={(date) => date < startOfToday() || date > addDays(startOfToday(), 7)}
                  locale={he}
                  dir="rtl"
                  initialFocus
                />
              </PopoverContent>
            </Popover>
          </div>

          <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
            <Clock className="h-3.5 w-3.5" />
            <span>זמן שליחה</span>
          </div>
          <div className="flex gap-2 items-end" dir="ltr">
            <div className="flex-1 space-y-1">
              <Label className="text-xs text-muted-foreground text-center block">שעה</Label>
              <Select value={scheduledHour} onValueChange={setScheduledHour}>
                <SelectTrigger className="text-right" dir="rtl">
                  <SelectValue placeholder="--" />
                </SelectTrigger>
                <SelectContent>
                  {HOURS.map((h) => (
                    <SelectItem key={h} value={h}>
                      {h}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <span className="text-xl font-bold text-muted-foreground select-none pb-2">:</span>
            <div className="flex-1 space-y-1">
              <Label className="text-xs text-muted-foreground text-center block">דקות</Label>
              <Select value={scheduledMinute} onValueChange={setScheduledMinute}>
                <SelectTrigger className="text-right" dir="rtl">
                  <SelectValue placeholder="--" />
                </SelectTrigger>
                <SelectContent className="max-h-64">
                  {MINUTES.map((m) => (
                    <SelectItem key={m} value={m}>
                      {m}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>
        </div>
      )}

      <Button
        onClick={handleScheduleCampaign}
        disabled={isDisabled}
        className="w-full"
        variant="outline"
        size="lg"
      >
        {scheduling ? (
          <>
            <Loader2 className="h-5 w-5 ml-2 animate-spin" />
            מתזמן...
          </>
        ) : (
          <>
            <CalendarClock className="h-5 w-5 ml-2" />
            תזמן קמפיין
          </>
        )}
      </Button>

      <AlertDialog open={confirmOpen} onOpenChange={setConfirmOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>אישור הפצת קמפיין</AlertDialogTitle>
            <AlertDialogDescription className="text-base">
              האם אתה בטוח בהפצת קמפיין? פעולה זאת אינה ניתנת לביטול
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>ביטול</AlertDialogCancel>
            <AlertDialogAction
              onClick={() => {
                setConfirmOpen(false);
                void handleStartCampaign();
              }}
              className="btn-gradient"
            >
              <Play className="h-4 w-4 ml-2" />
              אישור והפצה
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
