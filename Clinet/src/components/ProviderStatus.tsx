import { useAppStore } from '@/store/appStore';
import { STATUS_LABELS, type ProviderStatus } from '@/types';
import { Wifi, WifiOff, RefreshCw } from 'lucide-react';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';

const statusConfig: Record<ProviderStatus, { icon: React.ReactNode; className: string }> = {
  active: {
    icon: <Wifi className="h-5 w-5" />,
    className: 'status-active',
  },
  inactive: {
    icon: <WifiOff className="h-5 w-5" />,
    className: 'status-inactive',
  },
  pending: {
    icon: <RefreshCw className="h-5 w-5 animate-spin" />,
    className: 'status-pending',
  },
};

export function ProviderStatus() {
  const { providers, selectedProviderId, setSelectedProviderId } = useAppStore();

  const allActive = providers.every((p) => p.status === 'active');
  const someInactive = providers.some((p) => p.status === 'inactive');

  return (
    <div className="widget-card">
      <div className="mb-4">
        <h3 className="text-lg font-semibold text-foreground">סטטוס ספקים</h3>
      </div>

      {/* Overall Status */}
      <div
        className={`mb-4 p-3 rounded-lg ${
          allActive
            ? 'bg-success/10 border border-success/20'
            : someInactive
            ? 'bg-destructive/10 border border-destructive/20'
            : 'bg-warning/10 border border-warning/20'
        }`}
      >
        <p className={`text-sm font-medium ${allActive ? 'text-success' : someInactive ? 'text-destructive' : 'text-warning'}`}>
          {allActive ? 'כל הספקים פעילים ✓' : someInactive ? 'יש ספקים לא פעילים' : 'בודק סטטוס...'}
        </p>
      </div>

      {/* Provider List */}
      <div className="space-y-3">
        {providers.map((provider) => {
          const config = statusConfig[provider.status];
          return (
            <div
              key={provider.id}
              className="flex items-center justify-between p-3 bg-secondary/50 rounded-lg"
            >
              <div className="flex items-center gap-3">
                <div className={`p-2 rounded-full ${config.className}`}>
                  {config.icon}
                </div>
              <div>
                  <p className="font-medium text-foreground">{provider.nameHe}</p>
                  <p className="text-xs text-muted-foreground">{provider.name}</p>
                </div>
              </div>
              <div className="text-left">
                <span className={`status-badge ${config.className}`}>
                  {STATUS_LABELS[provider.status]}
                </span>
              </div>
            </div>
          );
        })}
      </div>

      {/* Provider Selection for Campaign */}
      <div className="mt-4 pt-4 border-t border-border">
        <label className="text-sm font-medium text-foreground mb-2 block">ספק לשליחת קמפיין</label>
        <Select value={selectedProviderId} onValueChange={setSelectedProviderId}>
          <SelectTrigger>
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {providers.map((p) => (
              <SelectItem key={p.id} value={p.id}>
                {p.nameHe} ({p.name})
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
    </div>
  );
}
