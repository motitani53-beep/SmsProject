import { useEffect, useState, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { useAppStore } from '@/store/appStore';
import { Header } from '@/components/Header';
import { CheckCircle, XCircle, Clock, ArrowRight } from 'lucide-react';
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

type ContactSendStatus = 'pending' | 'sent' | 'failed';

interface ContactRow {
  id: string;
  name: string;
  phone: string;
  message: string;
  status: ContactSendStatus;
}

export default function CampaignProgress() {
  const navigate = useNavigate();
  const { activeCampaign } = useAppStore();

  const [rows, setRows] = useState<ContactRow[]>([]);
  const [progress, setProgress] = useState(0);
  const intervalRef = useRef<NodeJS.Timeout | null>(null);
  const currentIndexRef = useRef(0);

  // Initialize rows from campaign contacts
  useEffect(() => {
    if (!activeCampaign) {
      navigate('/', { replace: true });
      return;
    }

    const initialRows: ContactRow[] = activeCampaign.contacts.map((contact) => {
      const name = (contact['name'] || contact['שם'] || contact['first_name'] || '-') as string;
      return {
        id: contact.id,
        name,
        phone: contact.phone,
        message: activeCampaign.message,
        status: 'pending',
      };
    });

    setRows(initialRows);
    currentIndexRef.current = 0;
    setProgress(0);
  }, [activeCampaign, navigate]);

  // Simulate sending one by one
  useEffect(() => {
    if (rows.length === 0) return;

    intervalRef.current = setInterval(() => {
      const idx = currentIndexRef.current;
      if (idx >= rows.length) {
        if (intervalRef.current) clearInterval(intervalRef.current);
        return;
      }

      // 95% success rate
      const status: ContactSendStatus = Math.random() > 0.05 ? 'sent' : 'failed';

      setRows((prev) => {
        const updated = [...prev];
        updated[idx] = { ...updated[idx], status };
        return updated;
      });

      currentIndexRef.current = idx + 1;
      const newProgress = Math.round(((idx + 1) / rows.length) * 100);
      setProgress(newProgress);
    }, 600);

    return () => {
      if (intervalRef.current) clearInterval(intervalRef.current);
    };
  }, [rows.length]);

  const sentCount = rows.filter((r) => r.status === 'sent').length;
  const failedCount = rows.filter((r) => r.status === 'failed').length;
  const pendingCount = rows.filter((r) => r.status === 'pending').length;
  const isComplete = progress === 100;

  return (
    <div className="min-h-screen bg-background">
      <Header />

      <main className="container mx-auto px-4 py-8 max-w-5xl">
        {/* Title + Back */}
        <div className="flex items-center justify-between mb-6">
          <h2 className="text-2xl font-bold text-foreground">
            התקדמות קמפיין{activeCampaign ? ` — ${activeCampaign.name}` : ''}
          </h2>
          <Button variant="outline" onClick={() => navigate('/')}>
            <ArrowRight className="h-4 w-4 ml-2" />
            חזרה לדף הראשי
          </Button>
        </div>

        {/* Progress Card */}
        <div className="widget-card mb-6">
          <div className="flex items-center justify-between mb-3">
            <span className="text-sm font-medium text-foreground">התקדמות שליחה</span>
            <span className="text-sm font-bold text-primary">{progress}%</span>
          </div>

          {/* Progress Bar */}
          <div className="w-full h-4 bg-secondary rounded-full overflow-hidden">
            <div
              className="h-full bg-primary rounded-full transition-all duration-300 ease-out"
              style={{ width: `${progress}%` }}
            />
          </div>

          {/* Stats Row */}
          <div className="flex gap-6 mt-4 text-sm">
            <div className="flex items-center gap-1.5">
              <CheckCircle className="h-4 w-4 text-success" />
              <span className="text-foreground">{sentCount} נשלח</span>
            </div>
            <div className="flex items-center gap-1.5">
              <XCircle className="h-4 w-4 text-destructive" />
              <span className="text-foreground">{failedCount} נכשל</span>
            </div>
            <div className="flex items-center gap-1.5">
              <Clock className="h-4 w-4 text-muted-foreground" />
              <span className="text-foreground">{pendingCount} ממתין</span>
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
          <h3 className="text-lg font-semibold text-foreground mb-4">פירוט שליחה</h3>

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
                  {rows.map((row, index) => (
                    <TableRow
                      key={row.id}
                      className={
                        row.status === 'failed'
                          ? 'bg-destructive/5'
                          : row.status === 'sent'
                          ? 'bg-success/5'
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
                        {row.status === 'sent' && (
                          <span className="inline-flex items-center gap-1 text-xs font-medium text-success">
                            <CheckCircle className="h-3.5 w-3.5" />
                            נשלח
                          </span>
                        )}
                        {row.status === 'failed' && (
                          <span className="inline-flex items-center gap-1 text-xs font-medium text-destructive">
                            <XCircle className="h-3.5 w-3.5" />
                            נכשל
                          </span>
                        )}
                        {row.status === 'pending' && (
                          <span className="inline-flex items-center gap-1 text-xs font-medium text-muted-foreground">
                            <Clock className="h-3.5 w-3.5" />
                            ממתין
                          </span>
                        )}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          </ScrollArea>
        </div>
      </main>
    </div>
  );
}
