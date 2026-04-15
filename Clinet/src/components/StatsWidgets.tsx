import { useAppStore } from '@/store/appStore';
import { CAMPAIGN_STATUS_LABELS } from '@/types';
import { formatDistanceToNow } from 'date-fns';
import { he } from 'date-fns/locale';
import { TrendingUp, Send, Users, Clock } from 'lucide-react';

export function StatsWidgets() {
  const { campaigns, importedContacts, smsTests } = useAppStore();

  const totalCampaigns = campaigns.length;
  const activeCampaigns = campaigns.filter((c) => c.status === 'running').length;
  const totalSent = campaigns.reduce((sum, c) => sum + c.totalSent, 0) + smsTests.filter(t => t.status === 'sent').length;
  const validContacts = importedContacts.filter((c) => c.isValid).length;

  const lastCampaign = campaigns[0];

  const stats = [
    {
      icon: <TrendingUp className="h-6 w-6" />,
      label: 'קמפיינים פעילים',
      value: activeCampaigns,
      subtext: `מתוך ${totalCampaigns} קמפיינים`,
      color: 'text-primary',
      bg: 'bg-primary/10',
    },
    {
      icon: <Send className="h-6 w-6" />,
      label: 'הודעות נשלחו',
      value: totalSent,
      subtext: 'בסך הכל',
      color: 'text-success',
      bg: 'bg-success/10',
    },
    {
      icon: <Users className="h-6 w-6" />,
      label: 'אנשי קשר תקינים',
      value: validContacts,
      subtext: `מתוך ${importedContacts.length}`,
      color: 'text-accent-foreground',
      bg: 'bg-accent',
    },
    {
      icon: <Clock className="h-6 w-6" />,
      label: 'קמפיין אחרון',
      value: lastCampaign ? CAMPAIGN_STATUS_LABELS[lastCampaign.status] : '-',
      subtext: lastCampaign
        ? formatDistanceToNow(new Date(lastCampaign.updatedAt), {
            addSuffix: true,
            locale: he,
          })
        : 'אין קמפיינים',
      color: 'text-warning',
      bg: 'bg-warning/10',
      isText: true,
    },
  ];

  return (
    <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
      {stats.map((stat, index) => (
        <div
          key={index}
          className="widget-card flex items-start gap-4 animate-fade-in"
          style={{ animationDelay: `${index * 100}ms` }}
        >
          <div className={`p-3 rounded-xl ${stat.bg} ${stat.color}`}>
            {stat.icon}
          </div>
          <div className="flex-1 min-w-0">
            <p className="text-sm text-muted-foreground">{stat.label}</p>
            <p className={`text-2xl font-bold ${stat.isText ? 'text-base' : ''}`}>
              {stat.value}
            </p>
            <p className="text-xs text-muted-foreground truncate">{stat.subtext}</p>
          </div>
        </div>
      ))}
    </div>
  );
}
