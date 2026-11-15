<?php
/**
 * Tests for the tag that loads the beacon.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Unit;

use Brain\Monkey\Functions;
use Dewiride\Analytics\WordPress\Audience;
use Dewiride\Analytics\WordPress\Settings;
use Dewiride\Analytics\WordPress\Tracker_Tag;

/**
 * Enqueueing the beacon and the attributes its tag carries.
 */
final class Tracker_Tag_Test extends Test_Case {

	/**
	 * Starts each test on an ordinary page read by somebody who is not signed in.
	 */
	protected function setUp(): void {
		parent::setUp();
		$this->given_settings();
		Functions\when( 'is_preview' )->justReturn( false );
		Functions\when( 'is_customize_preview' )->justReturn( false );
		Functions\when( 'is_embed' )->justReturn( false );
		Functions\when( 'is_user_logged_in' )->justReturn( false );
	}

	/**
	 * The tag hooks into enqueueing and into the attributes of printed scripts.
	 */
	public function test_it_hooks_enqueueing_and_attributes(): void {
		$tag = $this->tag();

		$tag->register();

		self::assertSame( 10, has_action( 'wp_enqueue_scripts', array( $tag, 'enqueue' ) ) );
		self::assertSame( 10, has_filter( 'wp_script_attributes', array( $tag, 'attributes' ) ) );
	}

	/**
	 * The beacon is enqueued async in the head, from the saved address, with no version.
	 */
	public function test_the_beacon_is_enqueued_async_in_the_head_without_a_version(): void {
		Functions\expect( 'wp_enqueue_script' )
			->once()
			->with(
				Tracker_Tag::HANDLE,
				self::ADDRESS . '/dw.js',
				array(),
				null,
				array(
					'strategy'  => 'async',
					'in_footer' => false,
				)
			);

		$this->tag()->enqueue();
	}

	/**
	 * Nothing is enqueued until the address and the Website ID are both saved.
	 */
	public function test_nothing_is_enqueued_until_counting_is_on(): void {
		$this->given_settings( self::ADDRESS, '' );
		Functions\expect( 'wp_enqueue_script' )->never();

		$this->tag()->enqueue();
	}

	/**
	 * Nothing is enqueued for a visitor who is not counted.
	 */
	public function test_nothing_is_enqueued_for_somebody_signed_in(): void {
		Functions\when( 'is_user_logged_in' )->justReturn( true );
		Functions\when( 'wp_get_current_user' )->justReturn( (object) array( 'roles' => array( 'administrator' ) ) );
		Functions\expect( 'wp_enqueue_script' )->never();

		$this->tag()->enqueue();
	}

	/**
	 * The beacon's tag gains its own attributes and the optimisers' opt-outs, and keeps its own.
	 */
	public function test_the_beacons_tag_carries_its_attributes(): void {
		$attributes = $this->tag()->attributes(
			array(
				'type'  => 'text/javascript',
				'src'   => self::ADDRESS . '/dw.js',
				'id'    => Tracker_Tag::ELEMENT_ID,
				'async' => true,
			)
		);

		self::assertIsArray( $attributes );
		self::assertSame( 'false', $attributes['data-cfasync'] );
		self::assertSame( self::SITE_ID, $attributes['data-site'] );
		self::assertSame( self::ADDRESS . '/collect', $attributes['data-collector'] );
		self::assertSame( '1', $attributes['data-no-optimize'] );
		self::assertSame( '1', $attributes['data-no-defer'] );
		self::assertSame( '1', $attributes['data-no-minify'] );
		self::assertSame( '1', $attributes['data-noptimize'] );
		self::assertSame( 'ignore', $attributes['data-jetpack-boost'] );
		self::assertTrue( $attributes['nowprocket'] );
		self::assertSame( self::ADDRESS . '/dw.js', $attributes['src'] );
		self::assertSame( 'text/javascript', $attributes['type'] );
		self::assertTrue( $attributes['async'] );
		self::assertArrayNotHasKey( 'integrity', $attributes );
		self::assertArrayNotHasKey( 'crossorigin', $attributes );
	}

	/**
	 * `data-cfasync` comes before `src`, where Cloudflare's Rocket Loader reads it, and wins over
	 * any value another plugin gave it.
	 */
	public function test_data_cfasync_comes_first_and_is_false(): void {
		$attributes = $this->tag()->attributes(
			array(
				'src'          => self::ADDRESS . '/dw.js',
				'id'           => Tracker_Tag::ELEMENT_ID,
				'data-cfasync' => 'true',
			)
		);

		self::assertIsArray( $attributes );
		self::assertSame( 'data-cfasync', array_key_first( $attributes ) );
		self::assertSame( 'false', $attributes['data-cfasync'] );
	}

	/**
	 * Every other script tag is left exactly as it was.
	 */
	public function test_other_tags_are_left_alone(): void {
		$other = array(
			'src' => 'https://example.com/theme.js',
			'id'  => 'theme-js',
		);

		self::assertSame( $other, $this->tag()->attributes( $other ) );
		self::assertSame( 'not attributes', $this->tag()->attributes( 'not attributes' ) );
	}

	/**
	 * The tag over the saved settings.
	 */
	private function tag(): Tracker_Tag {
		$settings = new Settings();

		return new Tracker_Tag( $settings, new Audience( $settings ) );
	}
}
