<?php
/**
 * Tests for the suggested privacy policy text.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

use Brain\Monkey\Functions;
use Dewiride\Analytics\WordPress\Privacy_Policy;
use Mockery;

/**
 * WordPress's privacy policy guide is offered a description of what is collected, the server's
 * reports included.
 */
final class Privacy_Policy_Test extends Test_Case {

	/**
	 * The suggestion is offered where WordPress collects them.
	 */
	public function test_the_suggestion_is_hooked_into_the_dashboard(): void {
		$policy = new Privacy_Policy();

		$policy->register();

		self::assertSame( 10, has_action( 'admin_init', array( $policy, 'suggest' ) ) );
	}

	/**
	 * The suggestion names the plugin and gives its text.
	 */
	public function test_the_suggestion_is_given_to_wordpress(): void {
		Functions\when( 'wp_kses_post' )->returnArg();
		Functions\expect( 'wp_add_privacy_policy_content' )->once()->with( 'Dewiride Analytics', Mockery::type( 'string' ) );

		( new Privacy_Policy() )->suggest();
	}

	/**
	 * The text says what is collected, what the server reports whatever a visitor chooses, and
	 * for how long things are kept.
	 */
	public function test_the_text_says_what_is_collected_and_kept(): void {
		$text = Privacy_Policy::text();

		self::assertStringStartsWith( '<p>', $text );
		self::assertStringContainsString( 'Never what you type', $text );
		self::assertStringContainsString( 'whatever you choose in a consent banner', $text );
		self::assertStringContainsString( 'No cookies are set', $text );
		self::assertStringContainsString( 'kept for 72 hours', $text );
		self::assertStringContainsString( 'looked up in DNS', $text );
		self::assertStringContainsString( 'kept for twelve months', $text );
		self::assertStringContainsString( 'https://analytics.dewiride.com/privacy', $text );
	}
}
