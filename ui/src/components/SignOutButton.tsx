import { LogOut } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { logout } from '@/api/auth'

interface SignOutButtonProps {
  onSignedOut: () => void
}

export function SignOutButton({ onSignedOut }: SignOutButtonProps) {
  async function handleClick() {
    await logout()
    onSignedOut()
  }

  return (
    <Button
      variant="ghost"
      size="sm"
      onClick={handleClick}
      aria-label="Sign out"
      title="Sign out"
    >
      <LogOut className="h-4 w-4" />
    </Button>
  )
}
