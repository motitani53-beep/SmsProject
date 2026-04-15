import { useState } from 'react';
import { useAppStore } from '@/store/appStore';
import { validatePhone } from '@/utils/validation';
import { Send, Loader2, CheckCircle, AlertCircle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { toast } from 'sonner';

export function QuickSend() {
  const { addSMSTest, providers } = useAppStore();
  const [phone, setPhone] = useState('');
  const [message] = useState('הודעת בדיקה');
  const [sending, setSending] = useState(false);
  const [phoneError, setPhoneError] = useState<string | null>(null);

  const allProvidersActive = providers.every((p) => p.status === 'active');

  const handlePhoneChange = (value: string) => {
    setPhone(value);
    if (value) {
      const validation = validatePhone(value);
      setPhoneError(validation.isValid ? null : validation.error || null);
    } else {
      setPhoneError(null);
    }
  };

  const handleSend = async () => {
    const validation = validatePhone(phone);
    if (!validation.isValid) {
      setPhoneError(validation.error || 'מספר לא תקין');
      return;
    }

    if (!allProvidersActive) {
      toast.error('לא ניתן לשלוח - יש ספקים לא פעילים');
      return;
    }

    setSending(true);

    // Simulate sending
    await new Promise((resolve) => setTimeout(resolve, 1500));

    // Simulate success/failure (90% success rate)
    const success = Math.random() > 0.1;

    addSMSTest({
      phone: phone,
      message,
      status: success ? 'sent' : 'failed',
    });

    if (success) {
      toast.success(`הודעה נשלחה בהצלחה ל-${phone}`);
      setPhone('');
    } else {
      toast.error('שליחת ההודעה נכשלה');
    }

    setSending(false);
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
        </div>

        {/* Send Button */}
        <Button
          onClick={handleSend}
          disabled={sending || !phone || !allProvidersActive}
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
    </div>
  );
}
