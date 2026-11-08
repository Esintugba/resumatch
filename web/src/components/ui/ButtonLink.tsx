import { Link, type LinkProps } from 'react-router';
import styles from './Button.module.css';
import { cx } from './cx';

/** A router link that looks like a Button, for actions that navigate. */
export function ButtonLink({
  tone = 'secondary',
  className,
  ...props
}: LinkProps & { tone?: 'primary' | 'secondary' }) {
  return <Link className={cx(styles.button, styles[tone], styles.link, className)} {...props} />;
}
