<?php
/**
 * Declares what the smoke theme supports.
 *
 * Like many themes written before WordPress printed HTML5 everywhere, it supports HTML5 markup for
 * some things and not for scripts. On such a theme WordPress 6.4 to 6.9 prints a script's `type`
 * and writes a boolean attribute as `name="name"`, which is the markup the floor run checks.
 *
 * @package Dewiride_Analytics
 */

add_action(
	'after_setup_theme',
	static function (): void {
		add_theme_support( 'title-tag' );
		add_theme_support( 'html5', array( 'search-form', 'comment-form', 'gallery', 'caption', 'style' ) );
	}
);
