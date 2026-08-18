import { createNavigation } from 'next-intl/navigation';
import { routing } from './routing';

/**
 * Language-aware replacements for the framework's own navigation.
 *
 * Using these rather than the originals is what keeps a link written once working in every
 * language: the prefix is added or left off according to the routing rules instead of being
 * spelled out at each call site.
 */
export const { Link, redirect, usePathname, useRouter, getPathname } = createNavigation(routing);
