import { useAppStore } from '@/store/appStore';
import { ROLE_LABELS, type UserRole } from '@/types';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { Button } from '@/components/ui/button';
import { MessageSquare, ChevronDown, User, Shield, ShieldCheck, LogOut } from 'lucide-react';

const roleIcons: Record<UserRole, React.ReactNode> = {
  super_admin: <ShieldCheck className="h-4 w-4" />,
  admin: <Shield className="h-4 w-4" />,
  user: <User className="h-4 w-4" />,
};

export function Header() {
  const { currentUser, setUserRole, logout } = useAppStore();

  const roles: UserRole[] = ['super_admin', 'admin', 'user'];

  return (
    <header className="header-gradient text-primary-foreground shadow-lg">
      <div className="container mx-auto px-6 py-4">
        <div className="flex items-center justify-between">
          {/* Logo & Title */}
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-primary-foreground/20 backdrop-blur-sm">
              <MessageSquare className="h-6 w-6" />
            </div>
            <div>
              <h1 className="text-xl font-bold">מערכת ניהול קמפיינים</h1>
              <p className="text-sm text-primary-foreground/80">SMS Campaign Manager</p>
            </div>
          </div>

          {/* User & Role Switcher */}
          <div className="flex items-center gap-4">
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button
                  variant="ghost"
                  className="flex items-center gap-2 bg-primary-foreground/10 hover:bg-primary-foreground/20 text-primary-foreground"
                >
                  {roleIcons[currentUser.role]}
                  <span>{ROLE_LABELS[currentUser.role]}</span>
                  <ChevronDown className="h-4 w-4" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end" className="w-48">
                {roles.map((role) => (
                  <DropdownMenuItem
                    key={role}
                    onClick={() => setUserRole(role)}
                    className={`flex items-center gap-2 cursor-pointer ${
                      currentUser.role === role ? 'bg-accent' : ''
                    }`}
                  >
                    {roleIcons[role]}
                    <span>{ROLE_LABELS[role]}</span>
                  </DropdownMenuItem>
                ))}
              </DropdownMenuContent>
            </DropdownMenu>
            <Button
              variant="ghost"
              size="icon"
              onClick={logout}
              className="text-primary-foreground hover:bg-primary-foreground/20"
              title="התנתק"
            >
              <LogOut className="h-4 w-4" />
            </Button>
          </div>
        </div>
      </div>
    </header>
  );
}
