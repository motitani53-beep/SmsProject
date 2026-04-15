import { Header } from '@/components/Header';
import { ProviderStatus } from '@/components/ProviderStatus';
import { CSVUpload } from '@/components/CSVUpload';
import { ContactsTable } from '@/components/ContactsTable';
import { QuickSend } from '@/components/QuickSend';
import { CampaignManager } from '@/components/CampaignManager';
import { StartCampaignButton } from '@/components/StartCampaignButton';
import { StatsWidgets } from '@/components/StatsWidgets';

const Index = () => {
  return (
    <div className="min-h-screen bg-background">
      <Header />
      
      <main className="container mx-auto px-4 py-8">
        {/* Stats Row */}
        <section className="mb-8">
          <StatsWidgets />
        </section>

        {/* Main Grid */}
        <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
          {/* Left Column - Provider Status & Quick Send */}
          <div className="lg:col-span-3 space-y-6">
            <ProviderStatus />
            <QuickSend />
            <StartCampaignButton />
          </div>

           {/* Center Column - CSV Upload & Contacts Table */}
          <div className="lg:col-span-6 space-y-6">
          {/* Right Column - Campaign Manager, CSV Upload & Contacts Table */}
          <div className="lg:col-span-9 space-y-6">
            <CampaignManager />
             <CSVUpload />
             <ContactsTable />
          </div>

          {/* Right Column - Campaign Manager */}
          <div className="lg:col-span-3">
            
           </div>
         </div>
         </div>
       </main>


      {/* Footer */}
      <footer className="mt-12 py-6 border-t border-border bg-card">
        <div className="container mx-auto px-4 text-center text-sm text-muted-foreground">
          <p>מערכת ניהול קמפייני SMS © 2026</p>
        </div>
      </footer>
    </div>
  );
};

export default Index;
