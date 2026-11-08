import type { HTMLAttributes } from 'react';
import styles from './Card.module.css';
import { cx } from './cx';

interface CardProps extends HTMLAttributes<HTMLElement> {
  width?: 'narrow' | 'wide';
}

export function Card({ width = 'narrow', className, ...props }: CardProps) {
  return <section className={cx(styles.card, styles[width], className)} {...props} />;
}
