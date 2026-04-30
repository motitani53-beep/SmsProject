import { useEffect, useMemo, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { useNavigate } from 'react-router-dom';
import { useAppStore } from '@/store/appStore';
import { Header } from '@/components/Header';
import { CheckCircle, XCircle, Clock, Hourglass, Check, ArrowRight, Trash2, AlertTriangle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { ScrollArea } from '@/components/ui/scroll-area';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip';
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
import { buttonVariants } from '@/components/ui/button';
import { SIGNALR_HUB_URL } from '@/config/api';
import type { CampaignDeliveryDetailDto } from '@/api/campaigns';
import { deleteCampaign } from '@/api/campaigns';
import { useCampaignDetail } from '@/hooks/useCampaignDetail';
import { toast } from 'sonner';
import { isAxiosError } from 'axios';

const CONFIRM_TOAST_STYLE: React.CSSProperties = {
  minWidth: '440px',
  fontSize: '16px',
  padding: '20px 24px',
  fontWeight: 600,
  color: '#dc2626',
  border: '2px solid #dc2626',
  background: '#fef2f2',
};

const SUCCESS_TOAST_STYLE: React.CSSProperties = {
  minWidth: '380px',
  fontSize: '16px',
  padding: '16px 20px',
  fontWeight: 600,
  color: '#16a34a',
  border: '2px solid #16a34a',
  background: '#f0fdf4',
};

const ERROR_TOAST_STYLE: React.CSSProperties = {
  minWidth: '380px',
  fontSize: '16px',
  padding: '16px 20px',
  fontWeight: 600,
  color: '#dc2626',
  border: '2px solid #dc2626',
  background: '#fef2f2',
};

type ContactSendStatus = 'pending' | 'sent' | 'delivered' | 'failed' | 'expired';

interface DeliveryDetailRow {
  deliveryId: number;
  name: string;
  phone: string;
  message: string;
  /** Raw numeric status from API / SignalR (matches DB delivery_details.status). */
  apiStatus: number;
}

function displayNameFromAdditional(additionalData: Record<string, unknown> | null | undefined): string {
  if (!additionalData || typeof additionalData !== 'object') return '-';
  const o = additionalData as Record<string, unknown>;
  const pick = (k: string) => {
    const v = o[k];
    return typeof v === 'string' && v.trim() ? v : null;
  };
  return (
    pick('name') ??
    pick('שם') ??
    pick('first_name') ??
    (Object.values(o).find((v) => typeof v === 'string' && String(v).trim()) as string | undefined) ??
    '-'
  );
}

function mapApiDeliveriesToRows(details: CampaignDeliveryDetailDto[]): DeliveryDetailRow[] {
  return details.map((d) => ({
    deliveryId: d.id,
    name: displayNameFromAdditional(d.additionalData as Record<string, unknown> | null | undefined),
    phone: d.phoneNumber,
    message: d.messageContent,
    apiStatus: typeof d.status === 'number' ? d.status : Number(d.status),
  }));
}

/** Maps DB status int to UI row style (0 pending, 4 sent, 1 delivered, 2|5 failed, 7 expired). */
function apiStatusToUiStatus(apiStatus: number): ContactSendStatus {
  if (apiStatus === 0 || apiStatus === 6) return 'pending';
  if (apiStatus === 3 || apiStatus === 4) return 'sent';
  if (apiStatus === 1) return 'delivered';
  if (apiStatus === 2 || apiStatus === 5) return 'failed';
  if (apiStatus === 7) return 'expired';
  return 'pending';
}

const TERMINAL_STATUSES = new Set([1, 2, 5, 7]);

/** Only Scheduled campaigns can be cancelled from this page. Processing is treated like In Progress (not cancellable here). */
function isApiCampaignScheduled(status: string | undefined): boolean {
  return (status ?? '').toLowerCase() === 'scheduled';
}

function isApiCampaignActivelySending(status: string | undefined): boolean {
  const s = (status ?? '').toLowerCase();
  return s === 'processing' || s === 'in progress';
}

export default function CampaignProgress() {
  const navigate = useNavigate();
  const { activeCampaign, resetCampaignForm, updateCampaign, setActiveCampaign } = useAppStore();

  const campaignId = activeCampaign ? Number(activeCampaign.id) : NaN;
  const { data, isSuccess } = useCampaignDetail(
    Number.isFinite(campaignId) && campaignId > 0 ? campaignId : null
  );

  const [deliveryDetails, setDeliveryDetails] = useState<DeliveryDetailRow[]>([]);
  const [now, setNow] = useState<number>(() => Date.now());
  const [cancelling, setCancelling] = useState(false);
  const [cancelDialogOpen, setCancelDialogOpen] = useState(false);

  // Tick every 5 seconds so the cancel button auto-hides ~1 minute before scheduled time.
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 5000);
    return () => clearInterval(id);
  }, []);

  const scheduledTimeMs = data?.scheduledTime ? new Date(data.scheduledTime).getTime() : null;
  const isScheduledStatus = isApiCampaignScheduled(data?.status);
  const canCancel =
    !cancelling &&
    isScheduledStatus &&
    scheduledTimeMs != null &&
    !isNaN(scheduledTimeMs) &&
    scheduledTimeMs - now > 60_000;

  const handleBackToHome = () => {
    resetCampaignForm();
    navigate('/');
  };

  const performCancel = async () => {
    if (!Number.isFinite(campaignId) || campaignId <= 0) return;
    setCancelling(true);
    try {
      await deleteCampaign(campaignId);
      toast.success('הקמפיין בוטל ונמחק', {
        position: 'top-center',
        duration: 2500,
        style: SUCCESS_TOAST_STYLE,
      });
      resetCampaignForm();
      navigate('/');
    } catch (err) {
      let message = 'לא ניתן היה לבטל את הקמפיין';
      if (isAxiosError(err)) {
        const data = err.response?.data as { error?: string } | undefined;
        if (data?.error) message = data.error;
      }
      toast.error(message, {
        position: 'top-center',
        duration: 5000,
        style: ERROR_TOAST_STYLE,
      });
      setCancelling(false);
    }
  };

  const handleRequestCancel = () => {
    if (!canCancel) return;
    setCancelDialogOpen(true);
  };

  useEffect(() => {
    if (!activeCampaign) {
      navigate('/', { replace: true });
      return;
    }
  }, [activeCampaign, navigate]);

  // Scheduled campaigns start as draft locally; once the API is Processing or In Progress, match immediate campaigns (running).
  useEffect(() => {
    if (!data?.status || !activeCampaign) return;
    if (!isApiCampaignActivelySending(data.status)) return;
    if (activeCampaign.status !== 'draft') return;
    const id = activeCampaign.id;
    updateCampaign(id, { status: 'running' });
    setActiveCampaign({ ...activeCampaign, status: 'running', updatedAt: new Date() });
  }, [data?.status, activeCampaign, updateCampaign, setActiveCampaign]);

  useEffect(() => {
    if (!data?.deliveryDetails?.length) {
      if (data?.deliveryDetails?.length === 0) setDeliveryDetails([]);
      return;
    }
    setDeliveryDetails(mapApiDeliveriesToRows(data.deliveryDetails));
  }, [data]);

  useEffect(() => {
    if (!Number.isFinite(campaignId) || campaignId <= 0 || !isSuccess) return;

    let cancelled = false;
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(SIGNALR_HUB_URL, { withCredentials: true })
      .withAutomaticReconnect()
      .build();

    connection.on('ReceiveStatusUpdate', (payload: unknown) => {
      console.log('SignalR Signal Received:', payload);
      if (payload == null || typeof payload !== 'object') return;
      const o = payload as Record<string, unknown>;
      const idRaw = o.deliveryId ?? o.DeliveryId;
      const statusRaw = o.status ?? o.Status;
      const id =
        typeof idRaw === 'number' ? idRaw : typeof idRaw === 'string' ? Number(idRaw) : Number.NaN;
      const status =
        typeof statusRaw === 'number'
          ? statusRaw
          : typeof statusRaw === 'string'
            ? Number(statusRaw)
            : Number.NaN;
      if (!Number.isFinite(id) || !Number.isFinite(status)) return;
      setDeliveryDetails((prev) => {
        const idx = prev.findIndex((row) => row.deliveryId === id);
        if (idx === -1) return prev;
        if (prev[idx].apiStatus === status) return prev;
        const next = prev.slice();
        next[idx] = { ...prev[idx], apiStatus: status };
        return next;
      });
    });

    void connection
      .start()
      .then(async () => {
        if (cancelled) {
          await connection.stop();
          return;
        }
        if (connection.state !== signalR.HubConnectionState.Connected) {
          console.warn('SignalR: start() resolved but state is not Connected:', connection.state);
          return;
        }
        await connection.invoke('JoinCampaign', campaignId);
      })
      .catch((e) => {
        console.warn('SignalR connect/join failed:', e);
      });

    connection.onreconnected(async () => {
      if (cancelled) return;
      try {
        if (connection.state === signalR.HubConnectionState.Connected) {
          await connection.invoke('JoinCampaign', campaignId);
        }
      } catch (e) {
        console.warn('SignalR re-join after reconnect failed:', e);
      }
    });

    return () => {
      cancelled = true;
      void connection.stop();
    };
  }, [campaignId, isSuccess]);

  const { progress, sentCount, deliveredCount, failedCount, pendingCount, expiredCount } = useMemo(() => {
    const total = deliveryDetails.length;
    if (total === 0) {
      return {
        progress: 0,
        sentCount: 0,
        deliveredCount: 0,
        failedCount: 0,
        pendingCount: 0,
        expiredCount: 0,
      };
    }
    let sent = 0;
    let delivered = 0;
    let failed = 0;
    let pending = 0;
    let expired = 0;
    for (const row of deliveryDetails) {
      const s = row.apiStatus;
      if (s === 3 || s === 4) sent++;
      else if (s === 1) delivered++;
      else if (s === 2 || s === 5) failed++;
      else if (s === 0 || s === 6) pending++;
      else if (s === 7) expired++;
    }
    // Progress reflects "out of pending" — counts sent (3,4) + delivered (1) + failed (2,5) + expired (7).
    // This way the bar moves the moment a message leaves the queue, not just when a delivery receipt arrives.
    const processed = total - pending;
    return {
      progress: Math.round((processed / total) * 100),
      sentCount: sent,
      deliveredCount: delivered,
      failedCount: failed,
      pendingCount: pending,
      expiredCount: expired,
    };
  }, [deliveryDetails]);

  const isComplete =
    !isScheduledStatus && pendingCount === 0 && deliveryDetails.length > 0;
  const isActivelyProcessing =
    !isComplete &&
    (isApiCampaignActivelySending(data?.status) ||
      (deliveryDetails.length > 0 && pendingCount > 0));

  if (!activeCampaign) {
    return null;
  }

  return (
    <div className="min-h-screen bg-background">
      <Header />

      <main className="container mx-auto px-4 py-8 max-w-5xl">
        {/* Title + Back */}
        <div className="flex items-center justify-between mb-6">
          <h2 className="text-2xl font-bold text-foreground">
            התקדמות קמפיין{activeCampaign ? ` — ${activeCampaign.name}` : ''}
          </h2>
          <Button variant="outline" onClick={handleBackToHome}>
            <ArrowRight className="h-4 w-4 ml-2" />
            חזרה לדף הראשי
          </Button>
        </div>

        {/* Progress Card */}
        <div className="widget-card mb-6">
          <div className="flex items-center justify-between mb-3">
            <span className="text-sm font-medium text-foreground">התקדמות שליחה</span>
            <span
              className={`text-sm font-bold flex items-center gap-1.5 transition-colors duration-300 ${
                isComplete ? 'text-success' : 'text-primary'
              }`}
            >
              {isComplete && <CheckCircle className="h-4 w-4" />}
              {progress}%
            </span>
          </div>

          {/* Progress Bar */}
          <div className="relative w-full h-5 bg-secondary rounded-full overflow-hidden shadow-inner">
            {/* Filled portion — turns green on complete, blue while sending. */}
            <div
              className={`relative h-full rounded-full overflow-hidden transition-[width,background-color] duration-500 ease-out ${
                isComplete ? 'bg-success' : 'bg-primary'
              }`}
              style={{ width: `${progress}%` }}
            >
              {isActivelyProcessing && progress > 0 && (
                <span className="progress-shimmer" aria-hidden="true" />
              )}
            </div>

            {/* Indeterminate sliding gradient — shown only when bar is empty but the system is actively working. */}
            {progress === 0 && isActivelyProcessing && (
              <span className="progress-indeterminate" aria-hidden="true" />
            )}
          </div>

          {/* Stats Row */}
          <div className="flex gap-6 mt-4 text-sm">
            <div className="flex items-center gap-1.5">
              <CheckCircle className="h-4 w-4 text-success" />
              <span className="text-foreground">{sentCount} נשלח</span>
            </div>
            <div className="flex items-center gap-1.5">
              <Tooltip>
                <TooltipTrigger asChild>
                  <span className="inline-flex items-center gap-1.5 cursor-default">
                    <Check className="h-4 w-4 text-blue-600" strokeWidth={3} />
                    <span className="text-foreground">{deliveredCount} נמסר</span>
                  </span>
                </TooltipTrigger>
                <TooltipContent>
                  <p>ההודעה נמסרה למכשיר היעד</p>
                </TooltipContent>
              </Tooltip>
            </div>
            <div className="flex items-center gap-1.5">
              <XCircle className="h-4 w-4 text-destructive" />
              <span className="text-foreground">{failedCount} נכשל</span>
            </div>
            <div className="flex items-center gap-1.5">
              <Clock className="h-4 w-4 text-muted-foreground" />
              <span className="text-foreground">{pendingCount} ממתין</span>
            </div>
            <div className="flex items-center gap-1.5">
              <Tooltip>
                <TooltipTrigger asChild>
                  <span className="inline-flex items-center gap-1.5 cursor-default">
                    <Hourglass className="h-4 w-4 text-amber-600" />
                    <span className="text-foreground">{expiredCount} פג תוקף</span>
                  </span>
                </TooltipTrigger>
                <TooltipContent>
                  <p>עברו 3 ימים ולא התקבלה תשובה</p>
                </TooltipContent>
              </Tooltip>
            </div>
          </div>

          {isComplete && (
            <div className="mt-4 p-3 bg-success/10 border border-success/20 rounded-lg">
              <p className="text-sm font-medium text-success">הקמפיין הושלם בהצלחה!</p>
            </div>
          )}
        </div>

        {/* Contacts Table */}
        <div className="widget-card">
          <div className="flex items-center justify-between mb-4">
            <h3 className="text-lg font-semibold text-foreground">פירוט שליחה</h3>
            {canCancel && (
              <Button
                variant="outline"
                size="sm"
                onClick={handleRequestCancel}
                className="text-destructive border-destructive/50 hover:bg-destructive/10 hover:text-destructive"
              >
                <Trash2 className="h-3.5 w-3.5 ml-1.5" />
                בטל קמפיין
              </Button>
            )}
          </div>

          <ScrollArea className="h-[500px] rounded-lg border">
            <div className="min-w-max">
              <Table>
                <TableHeader className="sticky top-0 bg-muted/80 backdrop-blur-sm">
                  <TableRow>
                    <TableHead className="w-12 text-center">#</TableHead>
                    <TableHead className="min-w-32">מנוי</TableHead>
                    <TableHead className="w-40 font-mono">טלפון</TableHead>
                    <TableHead className="min-w-48">הודעה</TableHead>
                    <TableHead className="w-28 text-center">סטטוס</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {deliveryDetails.map((row, index) => {
                    const ui = apiStatusToUiStatus(row.apiStatus);
                    return (
                      <TableRow
                        key={row.deliveryId}
                        className={
                          ui === 'failed'
                            ? 'bg-destructive/5'
                            : ui === 'sent'
                            ? 'bg-success/5'
                            : ui === 'delivered'
                            ? 'bg-blue-50'
                            : ui === 'expired'
                            ? 'bg-amber-50'
                            : ''
                        }
                      >
                        <TableCell className="text-center text-muted-foreground">
                          {index + 1}
                        </TableCell>
                        <TableCell className="font-medium">{row.name}</TableCell>
                        <TableCell className="font-mono text-sm">{row.phone}</TableCell>
                        <TableCell className="max-w-64 truncate text-sm text-muted-foreground">
                          {row.message}
                        </TableCell>
                        <TableCell className="text-center">
                          {ui === 'sent' && (
                            <span className="inline-flex items-center gap-1 text-xs font-medium text-success">
                              <CheckCircle className="h-3.5 w-3.5" />
                              נשלח
                            </span>
                          )}
                          {ui === 'delivered' && (
                            <Tooltip>
                              <TooltipTrigger asChild>
                                <span className="inline-flex items-center gap-1 text-xs font-bold text-blue-600 cursor-default">
                                  <Check className="h-3.5 w-3.5" strokeWidth={3} />
                                  נמסר
                                </span>
                              </TooltipTrigger>
                              <TooltipContent>
                                <p>ההודעה נמסרה למכשיר היעד</p>
                              </TooltipContent>
                            </Tooltip>
                          )}
                          {ui === 'failed' && (
                            <span className="inline-flex items-center gap-1 text-xs font-medium text-destructive">
                              <XCircle className="h-3.5 w-3.5" />
                              נכשל
                            </span>
                          )}
                          {ui === 'pending' && (
                            <span className="inline-flex items-center gap-1 text-xs font-medium text-muted-foreground">
                              <Clock className="h-3.5 w-3.5" />
                              ממתין
                            </span>
                          )}
                          {ui === 'expired' && (
                            <Tooltip>
                              <TooltipTrigger asChild>
                                <span className="inline-flex items-center gap-1 text-xs font-medium text-amber-600 cursor-default">
                                  <Hourglass className="h-3.5 w-3.5" />
                                  פג תוקף
                                </span>
                              </TooltipTrigger>
                              <TooltipContent>
                                <p>עברו 3 ימים ולא התקבלה תשובה</p>
                              </TooltipContent>
                            </Tooltip>
                          )}
                        </TableCell>
                      </TableRow>
                    );
                  })}
                </TableBody>
              </Table>
            </div>
          </ScrollArea>
        </div>
      </main>

      <AlertDialog open={cancelDialogOpen} onOpenChange={setCancelDialogOpen}>
        <AlertDialogContent className="max-w-2xl p-10 gap-6 border-2 border-destructive/30 shadow-2xl">
          <AlertDialogHeader className="space-y-4">
            <div className="mx-auto flex h-16 w-16 items-center justify-center rounded-full bg-destructive/10 ring-4 ring-destructive/20">
              <AlertTriangle className="h-9 w-9 text-destructive" />
            </div>
            <AlertDialogTitle className="text-3xl font-extrabold text-center text-destructive">
              האם לבטל את הקמפיין?
            </AlertDialogTitle>
            <AlertDialogDescription className="text-lg text-foreground/80 text-center leading-relaxed">
              קמפיין זה ימחק לגמרי ולא ישמר תיעוד. פעולה זו אינה ניתנת לשחזור.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter className="gap-3 sm:gap-3 pt-4">
            <AlertDialogCancel
              className={buttonVariants({ size: 'lg' }) + ' btn-gradient text-base font-semibold min-w-[180px]'}
            >
              אל תבטל קמפיין
            </AlertDialogCancel>
            <AlertDialogAction
              onClick={() => {
                setCancelDialogOpen(false);
                void performCancel();
              }}
              className={
                buttonVariants({ variant: 'destructive', size: 'lg' }) +
                ' text-base font-semibold min-w-[180px] border-2 border-destructive shadow-lg shadow-destructive/30 hover:shadow-destructive/50'
              }
            >
              <Trash2 className="h-4 w-4 ml-2" />
              כן, אני מאשר
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
