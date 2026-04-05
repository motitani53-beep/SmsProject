import { useState, useCallback, useRef } from 'react';
import Papa from 'papaparse';
import { useAppStore } from '@/store/appStore';
import { validatePhone } from '@/utils/validation';
import type { Contact } from '@/types';
import { Upload, FileSpreadsheet, CheckCircle, X, AlertCircle } from 'lucide-react';
import { findMissingPlaceholders } from '@/utils/messagePlaceholders';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';

interface ParsedData {
  columns: string[];
  rows: Record<string, string>[];
}

const ACCEPT = '.csv,text/csv,application/vnd.ms-excel';

export function CSVUpload() {
  const { setImportedData, importedContacts, clearImportedData, campaignFormMessage } = useAppStore();
  const inputRef = useRef<HTMLInputElement>(null);

  const [parsing, setParsing] = useState(false);
  const [parsedData, setParsedData] = useState<ParsedData | null>(null);
  const [phoneColumn, setPhoneColumn] = useState<string>('');
  const [error, setError] = useState<string | null>(null);
  const [dragActive, setDragActive] = useState(false);

  const handleClearData = useCallback(() => {
    clearImportedData();
    setParsedData(null);
    setPhoneColumn('');
    setError(null);
    if (inputRef.current) inputRef.current.value = '';
  }, [clearImportedData]);

  const processImport = useCallback((data: ParsedData, selectedPhoneColumn: string) => {
    if (!selectedPhoneColumn) return;

    // Validate that all [placeholders] in the message exist as CSV columns
    const missing = findMissingPlaceholders(campaignFormMessage, data.columns);
    if (missing.length > 0) {
      setError(
        `העמודות הבאות חסרות בקובץ ה-CSV אך מוזכרות בהודעה: ${missing.map((m) => `[${m}]`).join(', ')}`
      );
      return;
    }

    clearImportedData();

    const contacts: Contact[] = data.rows.map((row, index) => {
      const phone = row[selectedPhoneColumn] || '';
      const validation = validatePhone(phone);

      const contact: Contact = {
        id: `id-${Date.now()}-${index}-${Math.random().toString(36).substr(2, 9)}`,
        phone: phone,
        isValid: validation.isValid,
        validationError: validation.error,
      };

      data.columns.forEach((col) => {
        if (col !== selectedPhoneColumn) {
          contact[col] = row[col];
        }
      });

      return contact;
    });

    setImportedData(contacts, data.columns, selectedPhoneColumn);
    setParsedData(null);
    setPhoneColumn('');
    setError(null);
  }, [setImportedData, clearImportedData, campaignFormMessage]);

  const processFile = useCallback(
    async (file: File) => {
      // מחיקה מיידית של כל הנתונים הישנים – רק הקובץ החדש יוצג
      clearImportedData();
      setError(null);
      setParsing(true);
      setParsedData(null);

      if (inputRef.current) inputRef.current.value = '';

      try {
        // קריאה ישירה מהדיסק כ-text – מונעת cache של הדפדפן/Papa
        const text = await file.text();
        const results = Papa.parse<Record<string, string>>(text, {
          header: true,
          skipEmptyLines: true,
        });

        const columns = results.meta?.fields || [];
        const rows = results.data || [];

        if (columns.length === 0) {
          setError('הקובץ ריק או לא תקין');
          return;
        }

        const data = { columns, rows };

        const phoneColumnGuess = columns.find((col) =>
          ['phone', 'phone_number', 'טלפון', 'נייד', 'mobile', 'מספר'].some((term) =>
            col.toLowerCase().includes(term)
          )
        );

        if (phoneColumnGuess) {
          processImport(data, phoneColumnGuess);
        } else {
          setParsedData(data);
        }
      } catch (err) {
        setError('שגיאה בקריאת הקובץ: ' + (err instanceof Error ? err.message : String(err)));
      } finally {
        setParsing(false);
      }
    },
    [processImport, clearImportedData]
  );

  const onInputChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const file = e.target.files?.[0];
      if (!file) return;
      processFile(file);
    },
    [processFile]
  );

  const onDrop = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      e.stopPropagation();
      setDragActive(false);
      if (!campaignFormMessage.trim()) {
        setError('יש להזין תחילה את תוכן ההודעה לפני טעינת קובץ');
        return;
      }
      const file = e.dataTransfer.files?.[0];
      if (!file) return;
      if (!file.name.toLowerCase().endsWith('.csv')) {
        setError('נא להעלות קובץ CSV בלבד');
        return;
      }
      processFile(file);
    },
    [processFile, campaignFormMessage]
  );

  const onDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setDragActive(true);
  }, []);

  const onDragLeave = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    setDragActive(false);
  }, []);

  const hasMessage = campaignFormMessage.trim().length > 0;

  const openFileDialog = useCallback(() => {
    if (parsing) return;
    if (!hasMessage) return;
    inputRef.current?.click();
  }, [parsing, hasMessage]);

  return (
    <div className="widget-card">
      <div className="flex items-center justify-between mb-4">
        <h3 className="text-lg font-semibold text-foreground">טעינת קובץ CSV</h3>
        {importedContacts.length > 0 && (
          <Button variant="ghost" size="sm" onClick={handleClearData}>
            <X className="h-4 w-4 ml-1" />
            נקה נתונים
          </Button>
        )}
      </div>

      {!hasMessage && importedContacts.length === 0 && (
        <div className="flex items-center gap-2 p-3 bg-warning/10 border border-warning/20 rounded-lg mb-4">
          <AlertCircle className="h-5 w-5 text-warning" />
          <p className="text-sm text-warning">יש להזין תחילה את תוכן ההודעה לפני טעינת קובץ CSV</p>
        </div>
      )}

      {importedContacts.length > 0 ? (
        <div className="space-y-4">
          <div className="space-y-4 p-4 bg-success/10 border border-success/20 rounded-lg">
            <CheckCircle className="h-8 w-8 text-success" />
            <div>
              <p className="font-medium text-foreground">הקובץ נטען: {importedContacts.length} רשומות</p>
              <p className="text-sm text-success">{importedContacts.filter((c) => c.isValid).length} רשומות תקינות</p>
              {importedContacts.filter((c) => !c.isValid).length > 0 && (
                <p className="text-sm text-destructive">{importedContacts.filter((c) => !c.isValid).length} רשומות לא תקינות</p>
              )}
              <p className="text-sm text-muted-foreground">הנתונים עודכנו בטבלה למטה</p>
            </div>
          </div>
          <Button variant="outline" className="w-full" onClick={handleClearData}>
            העלה קובץ אחר
          </Button>
        </div>
      ) : (
        !parsedData && (
          <>
            <input
              ref={inputRef}
              type="file"
              accept={ACCEPT}
              onChange={onInputChange}
              className="hidden"
              tabIndex={-1}
              aria-hidden
            />
            <div
              role="button"
              tabIndex={0}
              onClick={openFileDialog}
              onKeyDown={(e) => e.key === 'Enter' && openFileDialog()}
              onDrop={onDrop}
              onDragOver={onDragOver}
              onDragLeave={onDragLeave}
              aria-disabled={!hasMessage}
              className={`border-2 border-dashed rounded-lg p-8 text-center transition-colors ${
                !hasMessage
                  ? 'border-border bg-muted/30 cursor-not-allowed opacity-60'
                  : dragActive
                  ? 'border-primary bg-accent cursor-pointer'
                  : 'border-border hover:border-primary/50 hover:bg-accent/50 cursor-pointer'
              }`}
            >
              {parsing ? (
                <div className="space-y-4">
                  <FileSpreadsheet className="h-12 w-12 mx-auto text-primary animate-pulse" />
                  <p className="text-muted-foreground">מעבד...</p>
                </div>
              ) : (
                <>
                  <Upload className="h-12 w-12 mx-auto text-muted-foreground mb-4" />
                  <p className="text-foreground font-medium mb-2">גרור קובץ CSV לכאן</p>
                  <p className="text-sm text-muted-foreground">או לחץ לבחירת קובץ</p>
                </>
              )}
            </div>
            {error && (
              <p className="mt-2 text-sm text-destructive" role="alert">
                {error}
              </p>
            )}
          </>
        )
      )}

      {parsedData && (
        <div className="mt-4 space-y-4">
          <div className="p-4 bg-accent/50 rounded-lg border">
            <p className="text-sm font-medium mb-3">איזו עמודה היא מספר הטלפון?</p>
            <Select value={phoneColumn} onValueChange={setPhoneColumn}>
              <SelectTrigger>
                <SelectValue placeholder="בחר עמודה..." />
              </SelectTrigger>
              <SelectContent>
                {parsedData.columns.map((col) => (
                  <SelectItem key={col} value={col}>
                    {col}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <Button
            onClick={() => processImport(parsedData, phoneColumn)}
            disabled={!phoneColumn}
            className="w-full"
          >
            אשר ייבוא
          </Button>
        </div>
      )}
    </div>
  );
}
