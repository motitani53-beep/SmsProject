import { useState } from 'react';
import { useNavigate, Navigate } from 'react-router-dom';
import { useAppStore } from '@/store/appStore';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { MessageSquare, Lock, User } from 'lucide-react';

const USERS = [
  { username: 'admin', password: '1234', name: 'מנהל ראשי', role: 'super_admin' as const },
  { username: 'manager', password: '1234', name: 'מנהל', role: 'admin' as const },
  { username: 'user', password: '1234', name: 'משתמש', role: 'user' as const },
];

export default function Login() {
  const { setAuth, isAuthenticated } = useAppStore();
  const navigate = useNavigate();
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');

  if (isAuthenticated) {
    return <Navigate to="/" replace />;
  }

  const handleLogin = (e: React.FormEvent) => {
    e.preventDefault();
    const found = USERS.find(u => u.username === username && u.password === password);
    if (found) {
      setAuth(true, { id: found.username, name: found.name, role: found.role });
      navigate('/', { replace: true });
    } else {
      setError('שם משתמש או סיסמא שגויים');
    }
  };

  return (
    <div className="min-h-screen bg-background flex items-center justify-center p-4">
      <div className="w-full max-w-sm">
        <div className="text-center mb-8">
          <div className="inline-flex h-14 w-14 items-center justify-center rounded-xl bg-primary text-primary-foreground mb-4">
            <MessageSquare className="h-8 w-8" />
          </div>
          <h1 className="text-2xl font-bold text-foreground">מערכת ניהול קמפיינים</h1>
          <p className="text-sm text-muted-foreground mt-1">SMS Campaign Manager</p>
        </div>

        <form onSubmit={handleLogin} className="widget-card space-y-4">
          <div className="space-y-2">
            <Label htmlFor="username">שם משתמש</Label>
            <div className="relative">
              <User className="absolute right-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
              <Input
                id="username"
                value={username}
                onChange={e => { setUsername(e.target.value); setError(''); }}
                className="pr-10"
                placeholder="הזן שם משתמש"
              />
            </div>
          </div>

          <div className="space-y-2">
            <Label htmlFor="password">סיסמא</Label>
            <div className="relative">
              <Lock className="absolute right-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
              <Input
                id="password"
                type="password"
                value={password}
                onChange={e => { setPassword(e.target.value); setError(''); }}
                className="pr-10"
                placeholder="הזן סיסמא"
              />
            </div>
          </div>

          {error && <p className="text-sm text-destructive">{error}</p>}

          <Button type="submit" className="w-full">התחבר</Button>

          <div className="border-t border-border pt-3">
            <p className="text-xs text-muted-foreground text-center mb-2">משתמשים לבדיקה:</p>
            <div className="text-xs text-muted-foreground space-y-1">
              <p><strong>admin</strong> / 1234 — מנהל ראשי</p>
              <p><strong>manager</strong> / 1234 — מנהל</p>
              <p><strong>user</strong> / 1234 — משתמש</p>
            </div>
          </div>
        </form>
      </div>
    </div>
  );
}
