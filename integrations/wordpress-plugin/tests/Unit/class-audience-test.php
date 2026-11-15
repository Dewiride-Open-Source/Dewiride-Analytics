<?php
/**
 * Tests for who is counted.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

use Brain\Monkey\Functions;
use Dewiride\Analytics\WordPress\Audience;
use Dewiride\Analytics\WordPress\Settings;

/**
 * Who is counted, and on which pages.
 */
final class Audience_Test extends Test_Case {

	/**
	 * Starts each test on an ordinary page read by somebody who is not signed in.
	 */
	protected function setUp(): void {
		parent::setUp();
		$this->given_settings( self::ADDRESS, self::SITE_ID, array( 'subscriber' ) );
		Functions\when( 'is_preview' )->justReturn( false );
		Functions\when( 'is_customize_preview' )->justReturn( false );
		Functions\when( 'is_embed' )->justReturn( false );
		Functions\when( 'is_user_logged_in' )->justReturn( false );
	}

	/**
	 * A visitor who is not signed in is counted.
	 */
	public function test_a_visitor_who_is_not_signed_in_is_counted(): void {
		self::assertTrue( $this->audience()->counts_current_visitor() );
	}

	/**
	 * Pages that are not being read as themselves count nobody.
	 *
	 * @return array<string, array{string}>
	 */
	public function pages_not_read_as_themselves(): array {
		return array(
			'a preview'      => array( 'is_preview' ),
			'the Customizer' => array( 'is_customize_preview' ),
			'an embed'       => array( 'is_embed' ),
		);
	}

	/**
	 * A preview, the Customizer and an embed are never counted.
	 *
	 * @dataProvider pages_not_read_as_themselves
	 *
	 * @param string $conditional The conditional tag that is true on such a page.
	 */
	public function test_a_page_not_read_as_itself_is_not_counted( string $conditional ): void {
		Functions\when( $conditional )->justReturn( true );

		self::assertFalse( $this->audience()->counts_current_visitor() );
	}

	/**
	 * The AMP copy of a page, which the AMP plugin serves, is never counted.
	 *
	 * Each test of the AMP plugin runs in a process of its own: stubbing `amp_is_request()`
	 * declares it, and a function once declared stays declared for every test that runs after it.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_an_amp_copy_is_not_counted(): void {
		Functions\when( 'amp_is_request' )->justReturn( true );

		self::assertFalse( $this->audience()->counts_current_visitor() );
	}

	/**
	 * A page the AMP plugin serves as itself is counted.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_a_page_the_amp_plugin_serves_as_itself_is_counted(): void {
		Functions\when( 'amp_is_request' )->justReturn( false );

		self::assertTrue( $this->audience()->counts_current_visitor() );
	}

	/**
	 * A page on a site without the AMP plugin is counted.
	 *
	 * @runInSeparateProcess
	 * @preserveGlobalState disabled
	 */
	public function test_a_page_on_a_site_without_the_amp_plugin_is_counted(): void {
		self::assertFalse( function_exists( 'amp_is_request' ) );
		self::assertTrue( $this->audience()->counts_current_visitor() );
	}

	/**
	 * Somebody signed in is left out when none of their roles is ticked.
	 */
	public function test_a_signed_in_person_is_left_out_by_default(): void {
		$this->given_signed_in( array( 'administrator' ) );

		self::assertFalse( $this->audience()->counts_current_visitor() );
	}

	/**
	 * Somebody signed in is counted when one of their roles is ticked.
	 */
	public function test_a_signed_in_person_with_a_ticked_role_is_counted(): void {
		$this->given_signed_in( array( 'editor', 'subscriber' ) );

		self::assertTrue( $this->audience()->counts_current_visitor() );
	}

	/**
	 * Somebody signed in without a role on this site is left out.
	 */
	public function test_a_signed_in_person_without_a_role_here_is_left_out(): void {
		$this->given_signed_in( array() );

		self::assertFalse( $this->audience()->counts_current_visitor() );
	}

	/**
	 * Roles that are not text never match a ticked role.
	 */
	public function test_roles_that_are_not_text_are_ignored(): void {
		self::assertFalse( $this->audience()->counts_roles( array( 0, null, array( 'subscriber' ) ) ) );
	}

	/**
	 * The audience over the saved settings.
	 */
	private function audience(): Audience {
		return new Audience( new Settings() );
	}

	/**
	 * Makes the visitor signed in with the given roles.
	 *
	 * @param string[] $roles The person's roles on this site.
	 */
	private function given_signed_in( array $roles ): void {
		Functions\when( 'is_user_logged_in' )->justReturn( true );
		Functions\when( 'wp_get_current_user' )->justReturn( (object) array( 'roles' => $roles ) );
	}
}
