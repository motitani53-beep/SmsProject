import { useAppStore } from '@/store/appStore';
import { AlertCircle, AlertTriangle, CheckCircle } from 'lucide-react';
import { findMissingPlaceholders, findUnusedColumns } from '@/utils/messagePlaceholders';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { ScrollArea } from '@/components/ui/scroll-area';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table';
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from '@/components/ui/tooltip';

type MessageLanguage = 'he' | 'en' | 'ar';
type SenderIdMode = 'random' | 'specific' | 'alphanumeric';

const languageConfig: Record<MessageLanguage, { label: string; dir: 'rtl' | 'ltr' }> = {
  he: { label: 'עברית', dir: 'rtl' },
  en: { label: 'English', dir: 'ltr' },
  ar: { label: 'العربية', dir: 'rtl' },
};

// --- Campaign Manager Component ---
export function CampaignManager() {
  const {
    importedContacts,
    providers,
    campaignFormName: campaignName,
    setCampaignFormName: setCampaignName,
    campaignFormMessage: message,
    setCampaignFormMessage: setMessage,
    campaignFormLanguage: language,
    setCampaignFormLanguage: setLanguage,
    campaignFormSenderIdMode: senderIdMode,
    setCampaignFormSenderIdMode: setSenderIdMode,
    campaignFormCustomSenderId: customSenderId,
    setCampaignFormCustomSenderId: setCustomSenderId,
    importedColumns,
    importedPhoneColumnName,
  } = useAppStore();

  const validContacts = importedContacts.filter((c) => c.isValid);
  const allProvidersActive = providers.every((p) => p.status === 'active');
  const missingPlaceholders =
    importedColumns.length > 0 ? findMissingPlaceholders(message, importedColumns) : [];
  const unusedColumns =
    importedColumns.length > 0
      ? findUnusedColumns(message, importedColumns, importedPhoneColumnName)
      : [];

  return (
    <div className="widget-card">
      <h3 className="text-lg font-semibold text-foreground mb-4">יצירת קמפיין חדש</h3>

      {!allProvidersActive && (
        <div className="flex items-center gap-2 p-3 bg-warning/10 border border-warning/20 rounded-lg mb-4">
          <AlertCircle className="h-5 w-5 text-warning" />
          <p className="text-sm text-warning">לא ניתן להפעיל - יש ספקים לא פעילים</p>
        </div>
      )}

      {validContacts.length === 0 && (
        <div className="flex items-center gap-2 p-3 bg-muted border border-border rounded-lg mb-4">
          <AlertCircle className="h-5 w-5 text-muted-foreground" />
          <p className="text-sm text-muted-foreground">אין אנשי קשר תקינים - העלה קובץ CSV</p>
        </div>
      )}

      {importedContacts.length > 0 && importedContacts.some((c) => !c.isValid) && (
        <div className="flex items-center gap-2 p-3 bg-destructive/10 border border-destructive/20 rounded-lg mb-4">
          <AlertCircle className="h-5 w-5 text-destructive" />
          <p className="text-sm text-destructive">יש מספרי טלפון לא תקינים ברשימה - יש לתקן את הקובץ ולהעלות מחדש</p>
        </div>
      )}

      {missingPlaceholders.length > 0 && (
        <div className="flex items-center gap-2 p-3 bg-destructive/10 border border-destructive/20 rounded-lg mb-4">
          <AlertCircle className="h-5 w-5 text-destructive" />
          <p className="text-sm text-destructive">
            העמודות הבאות חסרות בקובץ ה-CSV אך מוזכרות בהודעה: {missingPlaceholders.map((p) => `{${p}}`).join(', ')}
          </p>
        </div>
      )}

      {unusedColumns.length > 0 && (
        <div className="flex items-center gap-2 p-3 bg-warning/10 border border-warning/20 rounded-lg mb-4">
          <AlertTriangle className="h-5 w-5 text-warning" />
          <p className="text-sm text-warning">
            העמודות הבאות קיימות בקובץ ה-CSV אך לא מופיעות בהודעה: {unusedColumns.map((c) => `{${c}}`).join(', ')}
          </p>
        </div>
      )}

      <div className="space-y-4">
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          <div className="space-y-2">
            <Label htmlFor="campaign-name">שם הקמפיין</Label>
            <Input
              id="campaign-name"
              value={campaignName}
              onChange={(e) => setCampaignName(e.target.value)}
              placeholder="לדוגמה: קמפיין חודש ינואר"
            />
          </div>

          <div className="space-y-2">
            <Label>שפת ההודעה</Label>
            <Select value={language} onValueChange={(v) => setLanguage(v as MessageLanguage)}>
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {Object.entries(languageConfig).map(([key, config]) => (
                  <SelectItem key={key} value={key}>
                    {config.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-4 bg-muted/30 p-4 rounded-lg border border-border/50">
          <div className="space-y-2">
            <Label>סוג מספר שולח</Label>
            <Select 
              value={senderIdMode} 
              onValueChange={(v) => {
                setSenderIdMode(v as SenderIdMode);
                setCustomSenderId('');
              }}
            >
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="random">רנדומלי (Random)</SelectItem>
                <SelectItem value="specific">מספר ספציפי</SelectItem>
                <SelectItem value="alphanumeric">אלפא נומרי (שם עסק)</SelectItem>
              </SelectContent>
            </Select>
          </div>

          {senderIdMode !== 'random' && (
            <div className="space-y-2 animate-in fade-in slide-in-from-top-1 duration-200">
              <Label htmlFor="sender-id-value">
                {senderIdMode === 'specific' ? 'הזן מספר שולח' : 'הזן שם שולח (באנגלית)'}
              </Label>
              <Input
                id="sender-id-value"
                value={customSenderId}
                onChange={(e) => {
                  const val = e.target.value;
                  if (senderIdMode === 'specific') {
                    // Allow only digits
                    setCustomSenderId(val.replace(/\D/g, ''));
                  } else if (senderIdMode === 'alphanumeric') {
                    // Allow only A-Z, a-z, 0-9, dot, dash, underscore
                    setCustomSenderId(val.replace(/[^A-Za-z0-9.\-_]/g, ''));
                  } else {
                    setCustomSenderId(val);
                  }
                }}
                placeholder={senderIdMode === 'specific' ? '0541234567' : 'MyBusiness'}
                maxLength={senderIdMode === 'alphanumeric' ? 11 : 15}
                inputMode={senderIdMode === 'specific' ? 'numeric' : 'text'}
              />
            </div>
          )}
        </div>

        <div className="space-y-2">
          <Label htmlFor="message">תוכן ההודעה</Label>
          <Textarea
            id="message"
            value={message}
            onChange={(e) => setMessage(e.target.value)}
            placeholder="כתוב את ההודעה שברצונך לשלוח..."
            dir={languageConfig[language].dir}
            rows={4}
          />
        </div>

        {validContacts.length > 0 && (
          <div className="p-3 bg-accent/50 rounded-lg flex items-center justify-center">
            <p className="text-sm">
              <span className="font-medium">{validContacts.length}</span> אנשי קשר יקבלו את ההודעה
            </p>
          </div>
        )}
      </div>
    </div>
  );
}

// --- Contacts Table Component ---
export function ContactsTable() {
  const { importedContacts, importedColumns, importedPhoneColumnName } = useAppStore();

  if (importedContacts.length === 0) {
    return (
      <div className="widget-card">
        <h3 className="text-lg font-semibold text-foreground mb-4">רשימת אנשי קשר</h3>
        <div className="text-center py-12 text-muted-foreground">
          <p>אין נתונים להצגה</p>
          <p className="text-sm mt-1">העלה קובץ CSV להתחלה</p>
        </div>
      </div>
    );
  }

  const displayColumns = importedColumns.filter(
    (col) =>
      col !== 'isValid' &&
      col !== 'validationError' &&
      col !== importedPhoneColumnName
  );
  const phoneHeader = importedPhoneColumnName || 'טלפון';

  return (
    <div className="widget-card">
      <div className="flex items-center justify-between mb-4">
        <h3 className="text-lg font-semibold text-foreground">רשימת אנשי קשר</h3>
        <span className="text-sm text-muted-foreground">
          {importedContacts.length} רשומות
        </span>
      </div>

      <ScrollArea className="h-[400px] rounded-lg border">
        <div className="min-w-max">
          <Table>
            <TableHeader className="sticky top-0 bg-muted/80 backdrop-blur-sm">
              <TableRow>
                <TableHead className="w-12 text-center">#</TableHead>
                <TableHead className="w-12 text-center">סטטוס</TableHead>
                <TableHead className="w-40 font-mono">{phoneHeader}</TableHead>
                {displayColumns.map((col) => (
                  <TableHead key={col} className="min-w-32">
                    {col}
                  </TableHead>
                ))}
              </TableRow>
            </TableHeader>
            <TableBody>
              {importedContacts.map((contact, index) => (
                <TableRow
                  key={contact.id}
                  className={!contact.isValid ? 'bg-destructive/5' : ''}
                >
                  <TableCell className="text-center text-muted-foreground">
                    {index + 1}
                  </TableCell>
                  <TableCell className="text-center">
                    {contact.isValid ? (
                      <CheckCircle className="h-5 w-5 text-success mx-auto" />
                    ) : (
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <AlertCircle className="h-5 w-5 text-destructive mx-auto cursor-help" />
                        </TooltipTrigger>
                        <TooltipContent>
                          <p>{contact.validationError || 'שגיאה לא ידועה'}</p>
                        </TooltipContent>
                      </Tooltip>
                    )}
                  </TableCell>
                  <TableCell className="font-mono text-sm tabular-nums-hebrew">
                    {contact.phone}
                  </TableCell>
                  {displayColumns.map((col) => (
                    <TableCell key={col} className="max-w-48 truncate">
                      {String(contact[col] || '-')}
                    </TableCell>
                  ))}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      </ScrollArea>
    </div>
  );
}