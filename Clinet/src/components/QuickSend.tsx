import { useEffect, useState } from 'react';
import { useAppStore } from '@/store/appStore';
import { validatePhone } from '@/utils/validation';
import { Send, Loader2, CheckCircle, AlertCircle, AlertTriangle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { toast } from 'sonner';
import { postSendTest } from '@/api/sms';
import { isAxiosError } from 'axios';

const LANG_TO_API: Record<string, string> = {
  he: 'hebrew',
  en: 'english',
  ar: 'arabic',
};

export function QuickSend() {
  const {
    addSMSTest,
    providers,
    importedContacts,
    importedPhoneColumnName,
    campaignFormMessage,
    campaignFormLanguage,
    campaignFormSenderIdMode,
    campaignFormCustomSenderId,
  } = useAppStore();
  const [phone, setPhone] = useState('');
  const [sending, setSending] = useState(false);
  const [phoneError, setPhoneError] = useState<string | null>(null);
  /** When set, the centered success modal is visible for that phone number. */
  const [successPhone, setSuccessPhone] = useState<string | null>(null);
  /** When set, the centered error modal is visible with that message. */
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  // Auto-dismiss the success modal after 3 seconds.
  useEffect(() => {
    if (!successPhone) return;
    const t = setTimeout(() => setSuccessPhone(null), 3000);
    return () => clearTimeout(t);
  }, [successPhone]);

  // Auto-dismiss the error modal after 4 seconds (slightly longer so the user has time to read).
  useEffect(() => {
    if (!errorMessage) return;
    const t = setTimeout(() => setErrorMessage(null), 4000);
    return () => clearTimeout(t);
  }, [errorMessage]);

  const allProvidersActive = providers.every((p) => p.status === 'active');
  const hasMessage = campaignFormMessage.trim().length > 0;
  const hasCsv = importedContacts.length > 0;
  const phoneIsValid = validatePhone(phone).isValid;

  // Per spec: enable only when a message is entered AND a CSV is uploaded (same gate as Start Campaign).
  const canSend = hasMessage && hasCsv && phoneIsValid && !sending && allProvidersActive;

  const handlePhoneChange = (value: string) => {
    // Allow only digits to keep the input clean (matches CSV phone validation pattern).
    const digits = value.replace(/\D/g, '');
    setPhone(digits);
    if (digits) {
      const validation = validatePhone(digits);
      setPhoneError(validation.isValid ? null : validation.error || null);
    } else {
      setPhoneError(null);
    }
  };

  /** Returns the first CSV row's custom_fields (or any flat string columns) for placeholder substitution. */
  const buildCustomFieldsFromFirstRow = (): Record<string, string> => {
    const first = importedContacts[0];
    if (!first) return {};
    if (first.custom_fields && Object.keys(first.custom_fields).length > 0) {
      return { ...first.custom_fields };
    }
    const skip = new Set<string>(['id', 'phone', 'isValid', 'validationError', 'custom_fields']);
    if (importedPhoneColumnName) skip.add(importedPhoneColumnName);
    const out: Record<string, string> = {};
    for (const key of Object.keys(first)) {
      if (skip.has(key)) continue;
      const v = (first as Record<string, unknown>)[key];
      if (typeof v === 'string') out[key] = v;
    }
    return out;
  };

  const handleSend = async () => {
    const validation = validatePhone(phone);
    if (!validation.isValid) {
      setPhoneError(validation.error || 'מספר לא תקין');
      return;
    }
    if (!hasMessage) {
      toast.error('יש להזין תחילה את תוכן ההודעה');
      return;
    }
    if (!hasCsv) {
      toast.error('יש להעלות קובץ CSV לפני שליחת בדיקה');
      return;
    }
    if (!allProvidersActive) {
      toast.error('לא ניתן לשלוח - יש ספקים לא פעילים');
      return;
    }

    setSending(true);
    try {
      const senderValue =
        campaignFormSenderIdMode !== 'random' && campaignFormCustomSenderId.trim()
          ? campaignFormCustomSenderId.trim()
          : null;

      await postSendTest({
        phone_number: phone,
        message_content: campaignFormMessage,
        custom_fields: buildCustomFieldsFromFirstRow(),
        message_language: LANG_TO_API[campaignFormLanguage] ?? campaignFormLanguage,
        sender_value: senderValue,
      });

      // Spec: success popup ONLY upon 200 OK from Web API.
      addSMSTest({ phone, message: campaignFormMessage, status: 'sent' });
      setSuccessPhone(phone);
      setPhone('');
      setPhoneError(null);
    } catch (err) {
      let message = 'שליחת הודעת הבדיקה נכשלה';
      if (isAxiosError(err)) {
        const data = err.response?.data as { error?: string } | undefined;
        if (data?.error) message = data.error;
        else if (!err.response) message = 'השרת אינו מגיב — נסה שוב מאוחר יותר';
      }
      addSMSTest({ phone, message: campaignFormMessage, status: 'failed' });
      setErrorMessage(message);
    } finally {
      setSending(false);
    }
  };

  return (
    <div className="widget-card">
      <h3 className="text-lg font-semibold text-foreground mb-4">שליחה מהירה (בדיקה)</h3>

      {!allProvidersActive && (
        <div className="flex items-center gap-2 p-3 bg-warning/10 border border-warning/20 rounded-lg mb-4">
          <AlertCircle className="h-5 w-5 text-warning" />
          <p className="text-sm text-warning">לא ניתן לשלוח - יש ספקים לא פעילים</p>
        </div>
      )}

      <div className="space-y-4">
        {/* Phone Input */}
        <div className="space-y-2">
          <Label htmlFor="phone">מספר טלפון</Label>
          <div className="relative">
            <Input
              id="phone"
              type="tel"
              value={phone}
              onChange={(e) => handlePhoneChange(e.target.value)}
              placeholder="972544552211"
              className={`font-mono text-right ${phoneError ? 'border-destructive' : ''}`}
              dir="rtl"
            />
            {phone && !phoneError && (
              <CheckCircle className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-success" />
            )}
          </div>
          {phoneError && (
            <p className="text-sm text-destructive flex items-center gap-1">
              <AlertCircle className="h-3 w-3" />
              {phoneError}
            </p>
          )}
        </div>

{/* Test Message Info */}
        <div className="p-3 bg-secondary/50 rounded-lg space-y-1">
          <p className="text-sm font-medium text-foreground">הודעת בדיקה</p>
          <p className="text-xs text-muted-foreground">תוכן הודעת הבדיקה זהה לתוכן שיועבר למספר הראשון שהוגדר ברשימה.</p>
          {!hasMessage && (
            <p className="text-xs text-warning flex items-center gap-1 mt-1">
              <AlertCircle className="h-3 w-3" />
              יש להזין תחילה את תוכן ההודעה
            </p>
          )}
          {hasMessage && !hasCsv && (
            <p className="text-xs text-warning flex items-center gap-1 mt-1">
              <AlertCircle className="h-3 w-3" />
              יש להעלות קובץ CSV לפני שליחת בדיקה
            </p>
          )}
        </div>

        {/* Send Button */}
        <Button
          onClick={handleSend}
          disabled={!canSend}
          className="w-full btn-gradient"
        >
          {sending ? (
            <>
              <Loader2 className="h-4 w-4 ml-2 animate-spin" />
              שולח...
            </>
          ) : (
            <>
              <Send className="h-4 w-4 ml-2" />
              שלח הודעת בדיקה
            </>
          )}
        </Button>
      </div>

      {/* Centered success indicator — appears on 200 OK, auto-dismisses after 3s. */}
      {successPhone && (
        <div
          className="fixed inset-0 z-[100] flex items-center justify-center bg-black/40 backdrop-blur-sm animate-in fade-in duration-200"
          onClick={() => setSuccessPhone(null)}
          role="status"
          aria-live="polite"
        >
          <div
            className="relative bg-card border-2 border-success/30 rounded-2xl shadow-2xl p-10 max-w-md mx-4 text-center animate-in fade-in zoom-in-95 duration-300"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="mx-auto mb-5 flex h-24 w-24 items-center justify-center rounded-full bg-success/15 ring-4 ring-success/25 animate-in zoom-in-50 duration-500">
              <CheckCircle className="h-14 w-14 text-success" strokeWidth={2.5} />
            </div>
            <h2 className="text-2xl font-bold text-foreground mb-3">הודעת בדיקה נשלחה!</h2>
            <p className="text-base text-muted-foreground">
              ההודעה נשלחה בהצלחה למספר{' '}
              <span className="font-mono font-semibold text-foreground" dir="ltr">
                {successPhone}
              </span>
            </p>
          </div>
        </div>
      )}

      {/* Centered error indicator — mirrors the success modal in destructive red, auto-dismisses after 4s. */}
      {errorMessage && (
        <div
          className="fixed inset-0 z-[100] flex items-center justify-center bg-black/40 backdrop-blur-sm animate-in fade-in duration-200"
          onClick={() => setErrorMessage(null)}
          role="alert"
          aria-live="assertive"
        >
          <div
            className="relative bg-card border-2 border-destructive/40 rounded-2xl shadow-2xl p-10 max-w-md mx-4 text-center animate-in fade-in zoom-in-95 duration-300"
            onClick={(e) => e.stopPropagation()}
          >
            <div className="mx-auto mb-5 flex h-24 w-24 items-center justify-center rounded-full bg-destructive/15 ring-4 ring-destructive/25 animate-in zoom-in-50 duration-500">
              <AlertTriangle className="h-14 w-14 text-destructive" strokeWidth={2.5} />
            </div>
            <h2 className="text-2xl font-bold text-destructive mb-3">שגיאה בשליחה</h2>
            <p className="text-base text-foreground/80 leading-relaxed">{errorMessage}</p>
          </div>
        </div>
      )}
    </div>
  );
}
