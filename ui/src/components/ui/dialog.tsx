import * as React from 'react'
import { cn } from '@/lib/utils'

interface DialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  children: React.ReactNode
}

function Dialog({ open, onOpenChange, children }: DialogProps) {
  if (!open) return null

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center"
      onClick={() => onOpenChange(false)}
    >
      <div className="fixed inset-0 bg-black/50" aria-hidden="true" />
      <div
        className="relative z-50 w-full px-4 flex justify-center"
        onClick={(e) => e.stopPropagation()}
      >
        {children}
      </div>
    </div>
  )
}

type DialogSize = 'sm' | 'md' | 'lg' | 'xl'

const sizeClasses: Record<DialogSize, string> = {
  sm: 'max-w-md',
  md: 'max-w-2xl',
  lg: 'max-w-4xl',
  xl: 'max-w-6xl',
}

interface DialogContentProps extends React.HTMLAttributes<HTMLDivElement> {
  children: React.ReactNode
  size?: DialogSize
}

function DialogContent({ className, children, size = 'sm', ...props }: DialogContentProps) {
  return (
    <div
      className={cn(
        'bg-background rounded-lg border border-border shadow-lg p-6 w-full mx-auto max-w-[90vw] max-h-[90vh] overflow-y-auto',
        sizeClasses[size],
        className
      )}
      {...props}
    >
      {children}
    </div>
  )
}

function DialogHeader({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn('flex flex-col space-y-1.5 mb-4', className)}
      {...props}
    />
  )
}

function DialogTitle({ className, ...props }: React.HTMLAttributes<HTMLHeadingElement>) {
  return (
    <h2
      className={cn('text-lg font-semibold leading-none tracking-tight', className)}
      {...props}
    />
  )
}

function DialogDescription({ className, ...props }: React.HTMLAttributes<HTMLParagraphElement>) {
  return (
    <p
      className={cn('text-sm text-muted-foreground', className)}
      {...props}
    />
  )
}

function DialogFooter({ className, ...props }: React.HTMLAttributes<HTMLDivElement>) {
  return (
    <div
      className={cn('flex justify-end gap-2 mt-6', className)}
      {...props}
    />
  )
}

export { Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter }
