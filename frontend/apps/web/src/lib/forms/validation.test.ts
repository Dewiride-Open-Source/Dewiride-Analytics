import { describe, expect, it } from 'vitest';
import {
  checkEmail,
  checkHostname,
  checkPassword,
  checkPresent,
  SHORTEST_PASSWORD,
  tidyHostname,
} from '@/lib/forms/validation';

describe('email addresses', () => {
  it('asks for one when the box is empty', () => {
    expect(checkEmail('')).toBe('emailRequired');
  });

  it.each(['nobody', 'nobody@', '@example.com', 'nobody@example', 'no body@example.com'])(
    'refuses %s',
    (value) => {
      expect(checkEmail(value)).toBe('emailInvalid');
    },
  );

  it.each(['nobody@example.com', 'first.last+tag@sub.example.co.uk'])('accepts %s', (value) => {
    expect(checkEmail(value)).toBeNull();
  });
});

describe('passwords', () => {
  it('asks for one when the box is empty', () => {
    expect(checkPassword('')).toBe('passwordRequired');
  });

  it('refuses anything shorter than the engine accepts', () => {
    expect(checkPassword('a'.repeat(SHORTEST_PASSWORD - 1))).toBe('passwordTooShort');
  });

  it('accepts a passphrase of the required length', () => {
    expect(checkPassword('vermilion tractor almanac')).toBeNull();
  });
});

describe('things that simply have to be there', () => {
  it('treats whitespace as nothing at all', () => {
    expect(checkPresent('   ', 'organisationRequired')).toBe('organisationRequired');
  });

  it('accepts anything with a character in it', () => {
    expect(checkPresent(' My Blog ', 'organisationRequired')).toBeNull();
  });
});

describe('website addresses', () => {
  it('asks for one when the box is empty', () => {
    expect(checkHostname('')).toBe('websiteRequired');
  });

  it.each([
    'example',
    'example.',
    '.example.com',
    'exa mple.com',
    'example.c0m',
    '-bad.example.com',
  ])('refuses %s', (value) => {
    expect(checkHostname(value)).toBe('websiteInvalid');
  });

  it.each(['example.com', 'blog.example.co.uk', 'a-b.example.com'])('accepts %s', (value) => {
    expect(checkHostname(value)).toBeNull();
  });

  it('refuses a name longer than a hostname may be', () => {
    expect(checkHostname(`${'a'.repeat(250)}.com`)).toBe('websiteInvalid');
  });

  it('refuses a single label longer than a label may be', () => {
    expect(checkHostname(`${'a'.repeat(64)}.com`)).toBe('websiteInvalid');
  });
});

describe('reducing what was pasted to a hostname', () => {
  it.each([
    ['example.com', 'example.com'],
    ['  Example.COM  ', 'example.com'],
    ['https://example.com', 'example.com'],
    ['http://example.com/blog/', 'example.com'],
    ['https://example.com:8443/x', 'example.com'],
    ['https://user:secret@example.com/x', 'example.com'],
    ['example.com.', 'example.com'],
    ['https://example.com/a@b', 'example.com'],
    ['example.com?utm_source=x', 'example.com'],
    ['example.com#top', 'example.com'],
  ])('reads %s as %s', (typed, expected) => {
    expect(tidyHostname(typed)).toBe(expected);
  });
});
