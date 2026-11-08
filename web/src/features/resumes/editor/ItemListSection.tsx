import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { describeError } from '../../../api/describeError';
import { Alert } from '../../../components/ui/Alert';
import { Button } from '../../../components/ui/Button';
import { ConfirmDialog } from '../../../components/ui/ConfirmDialog';
import { messages } from '../../../i18n';
import styles from './ItemListSection.module.css';

const t = messages.items;

export interface ItemFormArgs<T> {
  /** null when adding. */
  item: T | null;
  onDirtyChange: (dirty: boolean) => void;
  /** Called after a successful save with the saved item's label. */
  onSaved: (label: string) => void;
  onCancel: () => void;
}

interface ItemListSectionProps<T> {
  /** Already sorted. */
  items: T[];
  getId: (item: T) => string;
  /** A short name for the item, used in labels and announcements. */
  label: (item: T) => string;
  renderItem: (item: T) => ReactNode;
  renderForm: (args: ItemFormArgs<T>) => ReactNode;
  /** What one item is called, for "… ekle". */
  noun: string;
  emptyText: string;
  limit: number;
  onDelete: (item: T) => Promise<void>;
  /** Saves the new sortOrder of one item; called for every item whose position changed. */
  onSortOrderChange: (item: T, sortOrder: number) => Promise<void>;
  /** After a failed change the list may be out of date. */
  onStale: () => Promise<unknown>;
  /** Reports unsaved changes in the open form to the editor. */
  onDirtyChange: (dirty: boolean) => void;
}

type Editing = { kind: 'new' } | { kind: 'item'; id: string } | null;

/**
 * A list section of the editor: add, edit, delete and reorder. One form is
 * open at a time; switching away from a changed form asks first. Deleting
 * asks first. Moves are announced and keep focus on the moved item.
 */
export function ItemListSection<T>({
  items,
  getId,
  label,
  renderItem,
  renderForm,
  noun,
  emptyText,
  limit,
  onDelete,
  onSortOrderChange,
  onStale,
  onDirtyChange,
}: ItemListSectionProps<T>) {
  const [editing, setEditing] = useState<Editing>(null);
  const [dirty, setDirty] = useState(false);
  const [pending, setPending] = useState<Editing | undefined>(undefined);
  const [toDelete, setToDelete] = useState<T | null>(null);
  const [busy, setBusy] = useState(false);
  const [status, setStatus] = useState<string | null>(null);
  const [failure, setFailure] = useState<string | null>(null);
  const focusAfterMove = useRef<string | null>(null);
  const listRef = useRef<HTMLUListElement>(null);

  const reportDirty = useCallback(
    (value: boolean) => {
      setDirty(value);
      onDirtyChange(value);
    },
    [onDirtyChange],
  );

  useEffect(() => {
    if (!focusAfterMove.current) return;
    const target = listRef.current?.querySelector<HTMLButtonElement>(`[data-move="${focusAfterMove.current}"]:not(:disabled)`);
    const fallback = listRef.current?.querySelector<HTMLButtonElement>(
      `[data-move^="${focusAfterMove.current.split(':')[0] ?? ''}:"]:not(:disabled)`,
    );
    (target ?? fallback)?.focus();
    focusAfterMove.current = null;
  }, [items]);

  function open(next: Editing) {
    reportDirty(false);
    setStatus(null);
    setFailure(null);
    setEditing(next);
  }

  function request(next: Editing) {
    if (dirty) setPending(next);
    else open(next);
  }

  async function move(index: number, direction: -1 | 1) {
    const target = index + direction;
    const moving = items[index];
    if (!moving || target < 0 || target >= items.length || busy) return;
    const reordered = [...items];
    reordered.splice(index, 1);
    reordered.splice(target, 0, moving);
    setBusy(true);
    setFailure(null);
    try {
      // Positions become 0..n-1; only items whose sortOrder changes are saved.
      for (const [position, entry] of reordered.entries()) {
        const current = Number((entry as { sortOrder?: number | string }).sortOrder ?? 0);
        if (current !== position) await onSortOrderChange(entry, position);
      }
      focusAfterMove.current = `${getId(moving)}:${direction < 0 ? 'up' : 'down'}`;
      setStatus(t.moved(label(moving), target + 1, items.length));
    } catch (error) {
      setFailure(describeError(error));
      await onStale();
    } finally {
      setBusy(false);
    }
  }

  async function confirmDelete(item: T) {
    setToDelete(null);
    setBusy(true);
    setFailure(null);
    try {
      await onDelete(item);
      if (editing?.kind === 'item' && editing.id === getId(item)) open(null);
      setStatus(t.deleted(label(item)));
    } catch (error) {
      setFailure(describeError(error));
      await onStale();
    } finally {
      setBusy(false);
    }
  }

  const formArgs = (item: T | null): ItemFormArgs<T> => ({
    item,
    onDirtyChange: reportDirty,
    onSaved: (saved) => {
      open(null);
      setStatus(t.saved(saved));
    },
    onCancel: () => {
      request(null);
    },
  });

  const full = items.length >= limit;

  return (
    <div>
      {failure !== null && (
        <Alert tone="error" role="alert">
          <p>{failure}</p>
        </Alert>
      )}
      {status !== null && (
        <Alert tone="success" role="status">
          <p>{status}</p>
        </Alert>
      )}

      <div className={styles.toolbar}>
        <span className={styles.count}>{t.count(items.length, limit)}</span>
        <Button
          tone="primary"
          disabled={full || busy || editing?.kind === 'new'}
          onClick={() => {
            request({ kind: 'new' });
          }}
        >
          {t.add(noun)}
        </Button>
      </div>

      {editing?.kind === 'new' && (
        <div className={styles.newForm} aria-label={t.newItem} role="group">
          {renderForm(formArgs(null))}
        </div>
      )}

      {items.length === 0 && editing?.kind !== 'new' && <p className={styles.empty}>{emptyText}</p>}

      {items.length > 0 && (
        <ul ref={listRef} className={styles.list}>
          {items.map((item, index) => {
            const id = getId(item);
            const name = label(item);
            const isEditing = editing?.kind === 'item' && editing.id === id;
            return (
              <li key={id} className={styles.item}>
                <div className={styles.row}>
                  <div className={styles.content}>{renderItem(item)}</div>
                  <div className={styles.actions}>
                    <Button
                      className={styles.iconButton}
                      disabled={busy || index === 0}
                      aria-label={t.moveUp(name)}
                      data-move={`${id}:up`}
                      onClick={() => void move(index, -1)}
                    >
                      ↑
                    </Button>
                    <Button
                      className={styles.iconButton}
                      disabled={busy || index === items.length - 1}
                      aria-label={t.moveDown(name)}
                      data-move={`${id}:down`}
                      onClick={() => void move(index, 1)}
                    >
                      ↓
                    </Button>
                    <Button
                      disabled={busy || isEditing}
                      aria-label={t.edit(name)}
                      onClick={() => {
                        request({ kind: 'item', id });
                      }}
                    >
                      {messages.common.edit}
                    </Button>
                    <Button
                      disabled={busy}
                      aria-label={t.delete(name)}
                      onClick={() => {
                        setToDelete(item);
                      }}
                    >
                      {t.deleteConfirm}
                    </Button>
                  </div>
                </div>
                {isEditing && <div className={styles.form}>{renderForm(formArgs(item))}</div>}
              </li>
            );
          })}
        </ul>
      )}

      {toDelete !== null && (
        <ConfirmDialog
          title={t.deleteTitle(label(toDelete))}
          description={t.deleteDescription}
          confirmLabel={t.deleteConfirm}
          cancelLabel={messages.common.cancel}
          onCancel={() => {
            setToDelete(null);
          }}
          onConfirm={() => void confirmDelete(toDelete)}
        />
      )}
      {pending !== undefined && (
        <ConfirmDialog
          title={t.unsavedTitle}
          description={t.unsavedDescription}
          confirmLabel={messages.unsaved.discard}
          cancelLabel={messages.common.cancel}
          onCancel={() => {
            setPending(undefined);
          }}
          onConfirm={() => {
            open(pending);
            setPending(undefined);
          }}
        />
      )}
    </div>
  );
}
