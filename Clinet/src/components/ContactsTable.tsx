import { useAppStore } from '@/store/appStore';
import { AlertCircle, CheckCircle } from 'lucide-react';
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

  // עמודות להצגה: כל עמודות הקובץ חוץ מעמודת הטלפון (שמוצגת בנפרד) ושדות פנימיים
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
                        <TooltipTrigger>
                          <AlertCircle className="h-5 w-5 text-destructive mx-auto" />
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
                      {String(contact[col] ?? '-')}
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
