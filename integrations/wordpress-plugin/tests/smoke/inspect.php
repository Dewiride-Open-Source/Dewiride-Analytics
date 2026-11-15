<?php
/**
 * Reads a page from standard input and checks it, inside the WordPress under test.
 *
 * Run by run.sh through WP-CLI, so the markup is read with WordPress's own HTML API and the
 * plugin's own classes are loaded:
 *
 *     SMOKE_EXPECT=present|absent|judged SMOKE_ADDRESS=… SMOKE_SITE_ID=… SMOKE_PAGE=… \
 *     SMOKE_MARKUP=legacy|html5 wp eval-file inspect.php < page.html
 *
 * `present` asserts the beacon's tag and the no-script image, by their attributes and in any
 * order; `absent` asserts neither is there; `judged` runs the tag check's parser over the page as
 * delivered, over copies changed the way optimisers, consent tools and pasted snippets change
 * it, and over the page as an address saved with http prints it, served over http and over
 * https. Prints `ok` when every assertion holds, and otherwise one line for each that failed.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress\Tests\Smoke;

use Dewiride\Analytics\WordPress\Tag_Check;
use Dewiride\Analytics\WordPress\Tracker_Tag;
use WP_HTML_Tag_Processor;

/**
 * Where the beacon is loaded from, after the analytics address.
 */
const BEACON = '/dw.js';

/**
 * Where the no-script image is loaded from, after the analytics address.
 */
const PIXEL = '/collect/pixel.gif';

/**
 * An environment variable run.sh sets, or an empty string.
 *
 * @param string $name The variable's name.
 */
function setting( string $name ): string {
	$value = getenv( $name );

	return is_string( $value ) ? $value : '';
}

/**
 * Every script tag that loads the beacon from the given address, each as its attributes.
 *
 * @param string $html       The page.
 * @param string $script_url The beacon's address.
 * @return list<array<string, string|true>>
 */
function beacon_scripts( string $html, string $script_url ): array {
	$processor = new WP_HTML_Tag_Processor( $html );
	$scripts   = array();

	while ( $processor->next_tag( array( 'tag_name' => 'script' ) ) ) {
		if ( $script_url === $processor->get_attribute( 'src' ) ) {
			$scripts[] = attributes_of( $processor );
		}
	}

	return $scripts;
}

/**
 * Every image inside a `<noscript>` that loads from the given address, each as its query.
 *
 * A `<noscript>` holds its markup as text to a parser that runs scripts, so each block's contents
 * are read on their own.
 *
 * @param string $html      The page.
 * @param string $pixel_url The no-script image's address, without its query.
 * @return list<array<mixed>>
 */
function pixel_queries( string $html, string $pixel_url ): array {
	$queries = array();

	preg_match_all( '~<noscript>(.*?)</noscript>~is', $html, $blocks );

	foreach ( $blocks[1] as $block ) {
		$processor = new WP_HTML_Tag_Processor( $block );

		while ( $processor->next_tag( array( 'tag_name' => 'img' ) ) ) {
			$source = $processor->get_attribute( 'src' );

			if ( is_string( $source ) && 0 === strpos( $source, $pixel_url . '?' ) ) {
				parse_str( (string) wp_parse_url( $source, PHP_URL_QUERY ), $query );
				$queries[] = $query;
			}
		}
	}

	return $queries;
}

/**
 * The attributes of the tag the processor stands on.
 *
 * @param WP_HTML_Tag_Processor $processor A processor standing on a tag.
 * @return array<string, string|true>
 */
function attributes_of( WP_HTML_Tag_Processor $processor ): array {
	$attributes = array();

	foreach ( $processor->get_attribute_names_with_prefix( '' ) ?? array() as $name ) {
		$value = $processor->get_attribute( $name );

		if ( is_string( $value ) || true === $value ) {
			$attributes[ $name ] = $value;
		}
	}

	return $attributes;
}

/**
 * The attributes the beacon's tag must carry, as WordPress writes them.
 *
 * A theme without HTML5 script support gets `type="text/javascript"` and boolean attributes
 * written as `name="name"` from WordPress 6.4 to 6.9; otherwise there is no type and a boolean
 * attribute is its name alone.
 *
 * @param string $address The analytics address.
 * @param string $site_id The Website ID.
 * @param string $markup  `legacy` or `html5`.
 * @return array<string, string|true|null>
 */
function expected_attributes( string $address, string $site_id, string $markup ): array {
	$legacy = 'legacy' === $markup;

	return array(
		'id'                 => Tracker_Tag::ELEMENT_ID,
		'data-site'          => $site_id,
		'data-collector'     => $address . '/collect',
		'data-cfasync'       => 'false',
		'data-no-optimize'   => '1',
		'data-no-defer'      => '1',
		'data-no-minify'     => '1',
		'data-noptimize'     => '1',
		'data-jetpack-boost' => 'ignore',
		'type'               => $legacy ? 'text/javascript' : null,
		'async'              => $legacy ? 'async' : true,
		'nowprocket'         => $legacy ? 'nowprocket' : true,
		'integrity'          => null,
		'crossorigin'        => null,
	);
}

/**
 * What is wrong with a page a counted visitor received.
 *
 * @param string $html    The page.
 * @param string $address The analytics address.
 * @param string $site_id The Website ID.
 * @param string $page    The address of the page.
 * @param string $markup  `legacy` or `html5`, for how WordPress writes the tag.
 * @return list<string>
 */
function present_failures( string $html, string $address, string $site_id, string $page, string $markup ): array {
	$failures = array();
	$scripts  = beacon_scripts( $html, $address . BEACON );
	$queries  = pixel_queries( $html, $address . PIXEL );

	if ( 1 !== count( $scripts ) ) {
		$failures[] = sprintf( 'expected one script loading %s/dw.js, found %d', $address, count( $scripts ) );
	} else {
		foreach ( expected_attributes( $address, $site_id, $markup ) as $name => $value ) {
			if ( ( $scripts[0][ $name ] ?? null ) !== $value ) {
				$failures[] = sprintf( 'the script\'s %s is %s, not %s', $name, (string) wp_json_encode( $scripts[0][ $name ] ?? null ), (string) wp_json_encode( $value ) );
			}
		}
	}

	if ( 1 !== count( $queries ) ) {
		$failures[] = sprintf( 'expected one no-script image, found %d', count( $queries ) );
	} elseif ( ( $queries[0]['site'] ?? null ) !== $site_id || ( $queries[0]['u'] ?? null ) !== $page ) {
		$failures[] = sprintf( 'the no-script image names %s', (string) wp_json_encode( $queries[0] ) );
	}

	return $failures;
}

/**
 * What is wrong with a page somebody who is not counted received.
 *
 * @param string $html    The page.
 * @param string $address The analytics address.
 * @return list<string>
 */
function absent_failures( string $html, string $address ): array {
	$failures = array();

	if ( array() !== beacon_scripts( $html, $address . BEACON ) ) {
		$failures[] = 'the page carries the beacon';
	}

	if ( array() !== pixel_queries( $html, $address . PIXEL ) ) {
		$failures[] = 'the page carries the no-script image';
	}

	return $failures;
}

/**
 * The page with the plugin's tag changed by a callback.
 *
 * @param string   $html   The page.
 * @param callable $change Changes the tag the processor stands on.
 */
function with_tag_changed( string $html, callable $change ): string {
	$processor = new WP_HTML_Tag_Processor( $html );

	while ( $processor->next_tag( array( 'tag_name' => 'script' ) ) ) {
		if ( Tracker_Tag::ELEMENT_ID === $processor->get_attribute( 'id' ) ) {
			$change( $processor );
		}
	}

	return $processor->get_updated_html();
}

/**
 * What is wrong with the tag check's reading of the page, and of copies of it changed the way
 * visitors' pages get changed.
 *
 * Each case is judged with the address its tag was printed for and the address the page was
 * served from. The last two are the page as it would be with the address saved with http, before
 * the site moved to https: the tag is printed as it should be, and browsers refuse it only on a
 * page served over https.
 *
 * @param string $html    The page as delivered.
 * @param string $address The analytics address.
 * @param string $site_id The Website ID.
 * @param string $page    The address the page was served from.
 * @return list<string>
 */
function judged_failures( string $html, string $address, string $site_id, string $page ): array {
	$typed = static function ( string $type ): callable {
		return static function ( WP_HTML_Tag_Processor $processor ) use ( $type ): void {
			$processor->set_attribute( 'type', $type );
		};
	};

	$http_address = (string) preg_replace( '~^https:~', 'http:', $address );
	$http_html    = with_tag_changed(
		$html,
		static function ( WP_HTML_Tag_Processor $processor ) use ( $http_address ): void {
			$processor->set_attribute( 'src', $http_address . BEACON );
		}
	);

	$cases = array(
		'as delivered'                         => array( $html, $address, $page, Tag_Check::INTACT, '' ),
		'delayed by WP Rocket'                 => array( with_tag_changed( $html, $typed( 'rocketlazyloadscript' ) ), $address, $page, Tag_Check::ALTERED, Tag_Check::WP_ROCKET ),
		'delayed by LiteSpeed Cache'           => array( with_tag_changed( $html, $typed( 'litespeed/javascript' ) ), $address, $page, Tag_Check::ALTERED, Tag_Check::LITESPEED_CACHE ),
		'held by a consent tool'               => array( with_tag_changed( $html, $typed( 'text/plain' ) ), $address, $page, Tag_Check::ALTERED, Tag_Check::CONSENT_TOOL ),
		'copied to the site\'s host'           => array(
			with_tag_changed(
				$html,
				static function ( WP_HTML_Tag_Processor $processor ): void {
					$processor->set_attribute( 'src', 'https://example.test/wp-content/cache/dw.js' );
				}
			),
			$address,
			$page,
			Tag_Check::ALTERED,
			Tag_Check::OPTIMISER,
		),
		'pasted into the theme again'          => array(
			// phpcs:ignore WordPress.WP.EnqueuedResources.NonEnqueuedScript -- The snippet an owner pastes into a theme by hand, which is what this case is about.
			str_replace( '</body>', '<script defer src="' . esc_url( $address . BEACON ) . '" data-site="' . esc_attr( $site_id ) . '"></script></body>', $html ),
			$address,
			$page,
			Tag_Check::DUPLICATE,
			'',
		),
		'never printed'                        => array(
			with_tag_changed(
				$html,
				static function ( WP_HTML_Tag_Processor $processor ): void {
					$processor->set_attribute( 'id', 'theme-js' );
					$processor->set_attribute( 'src', 'https://example.test/theme.js' );
				}
			),
			$address,
			$page,
			Tag_Check::MISSING,
			'',
		),
		'loading over http, served over https' => array( $http_html, $http_address, (string) preg_replace( '~^http:~', 'https:', $page ), Tag_Check::INSECURE, '' ),
		'loading over http, served over http'  => array( $http_html, $http_address, (string) preg_replace( '~^https:~', 'http:', $page ), Tag_Check::INTACT, '' ),
	);

	$failures = array();

	foreach ( $cases as $name => $case ) {
		$expected = array(
			'outcome' => $case[3],
			'tool'    => $case[4],
		);
		$judged   = Tag_Check::inspect( $case[0], $case[1], $site_id, $case[2] );

		if ( $expected !== $judged ) {
			$failures[] = sprintf( 'a page %s was judged %s', $name, (string) wp_json_encode( $judged ) );
		}
	}

	return $failures;
}

/**
 * Runs the assertions run.sh asked for and prints what failed, or `ok`.
 */
function main(): void {
	$html    = (string) stream_get_contents( STDIN );
	$address = setting( 'SMOKE_ADDRESS' );
	$site_id = setting( 'SMOKE_SITE_ID' );

	switch ( setting( 'SMOKE_EXPECT' ) ) {
		case 'present':
			$failures = present_failures( $html, $address, $site_id, setting( 'SMOKE_PAGE' ), setting( 'SMOKE_MARKUP' ) );
			break;
		case 'absent':
			$failures = absent_failures( $html, $address );
			break;
		case 'judged':
			$failures = judged_failures( $html, $address, $site_id, setting( 'SMOKE_PAGE' ) );
			break;
		default:
			$failures = array( 'SMOKE_EXPECT must be present, absent or judged' );
	}

	// phpcs:ignore WordPress.Security.EscapeOutput.OutputNotEscaped -- Plain text for the terminal running the harness.
	echo array() === $failures ? "ok\n" : implode( "\n", $failures ) . "\n";
}

main();
