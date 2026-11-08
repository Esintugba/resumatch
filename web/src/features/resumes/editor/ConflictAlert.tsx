import { useState } from 'react';
import { Alert } from '../../../components/ui/Alert';
import { Button } from '../../../components/ui/Button';
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog';
import { messages } from '../../../i18n';

const t = messages.conflict;

/**
 * Shown after a 412: the user's text stays in the form and nothing is
 * overwritten. Loading the saved version discards that text, so it asks first.
 */
export function ConflictAlert({ onReload, reloading }: { onReload: () => void; reloading: boolean }) {
  const [confirming, setConfirming] = useState(false);
  return (
    <>
      <Alert
        tone="warning"
        role="alert"
        title={t.title}
        actions={
          <Button tone="primary" disabled={reloading} onClick={() => {
            setConfirming(true);
          }}>
            {reloading ? messages.common.loading : t.reload}
          </Button>
        }
      >
        <p>{t.description}</p>
      </Alert>
      {confirming && (
        <ConfirmDialog
          title={t.confirmTitle}
          description={t.confirmDescription}
          confirmLabel={t.confirm}
          cancelLabel={messages.common.cancel}
          onCancel={() => {
            setConfirming(false);
          }}
          onConfirm={() => {
            setConfirming(false);
            onReload();
          }}
        />
      )}
    </>
  );
}
