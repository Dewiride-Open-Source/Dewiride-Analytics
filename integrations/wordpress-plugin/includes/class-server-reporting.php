<?php
/**
 * The site's own server reporting what it answers.
 *
 * @package Dewiride_Analytics
 */

namespace Dewiride\Analytics\WordPress;

use Exception;

if ( ! defined( 'ABSPATH' ) ) {
	exit;
}

/**
 * Reports each request worth reporting once it has been answered, and names the pages browsers
 * navigate to so that the browser's account of a page can be matched to the server's.
 *
 * A page is named in a `Server-Timing` header, which the tracker reads back from the browser.
 * Only a browser navigating to a page the server will report is given a name, and only once the
 * installation has shown it reads names safely, by naming the website in an answer.
 *
 * The report is sent at the very end of the request. Where PHP runs under PHP-FPM or LiteSpeed,
 * the page is finished first, so the visitor never waits for it. Anywhere else the visitor's page
 * is held open while the report is sent, which slows every page a crawler reads, search engines'
 * included — so there reporting is off unless the settings say otherwise.
 *
 * Reports also stop while the tag is kept from reaching readers, because a reader whose tag never
 * ran would be reported as a program that does not run scripts.
 */
final class Server_Reporting {

	/**
	 * The name the page's timing is given, which the tracker looks for.
	 */
	private const TIMING = 'dw';

	/**
	 * The plugin's settings.
	 *
	 * @var Settings
	 */
	private $settings;

	/**
	 * The server key.
	 *
	 * @var Server_Key
	 */
	private $server_key;

	/**
	 * How reports have been going.
	 *
	 * @var Reporting_Status
	 */
	private $status;

	/**
	 * What is reported of the request.
	 *
	 * @var Request_Report
	 */
	private $report;

	/**
	 * Where reports are sent.
	 *
	 * @var Collector_Client
	 */
	private $client;

	/**
	 * The check of the tag visitors receive.
	 *
	 * @var Tag_Check
	 */
	private $tag_check;

	/**
	 * The name the page was sent under, or an empty string.
	 *
	 * @var string
	 */
	private $name = '';

	/**
	 * Whether reports are being sent, once worked out for this request.
	 *
	 * @var bool|null
	 */
	private $sending;

	/**
	 * Creates the reporting.
	 *
	 * @param Settings         $settings   The plugin's settings.
	 * @param Server_Key       $server_key The server key.
	 * @param Reporting_Status $status     How reports have been going.
	 * @param Request_Report   $report     What is reported of the request.
	 * @param Collector_Client $client     Where reports are sent.
	 * @param Tag_Check        $tag_check  The check of the tag visitors receive.
	 */
	public function __construct( Settings $settings, Server_Key $server_key, Reporting_Status $status, Request_Report $report, Collector_Client $client, Tag_Check $tag_check ) {
		$this->settings   = $settings;
		$this->server_key = $server_key;
		$this->status     = $status;
		$this->report     = $report;
		$this->client     = $client;
		$this->tag_check  = $tag_check;
	}

	/**
	 * Hooks the naming into the page's headers and the report into the very end of the request.
	 */
	public function register(): void {
		add_action( 'send_headers', array( $this, 'name_page' ) );
		add_filter( 'wp_redirect', array( $this, 'unname_redirect' ), PHP_INT_MAX );
		add_action( 'shutdown', array( $this, 'report' ), PHP_INT_MAX );
	}

	/**
	 * Whether this server can finish a page before it sends the page's report.
	 */
	public static function finishes_pages_first(): bool {
		return function_exists( 'fastcgi_finish_request' ) || function_exists( 'litespeed_finish_request' );
	}

	/**
	 * Whether everything the reports need is in place: counting is on, there is a key, reporting
	 * is switched on for this server, the address may be sent a key, the tag reaches readers, and
	 * nothing has stopped or paused the reports.
	 *
	 * The settings are read first, so a site without a key costs nothing more than reading them.
	 */
	public function is_sending(): bool {
		if ( null === $this->sending ) {
			$this->sending = $this->settings->is_complete()
				&& '' !== $this->settings->key_ending()
				&& $this->settings->reports_server( self::finishes_pages_first() )
				&& Private_Host::may_receive_secrets( $this->settings->server_address() )
				&& ! Tag_Check::holds_back( $this->tag_check->last_result() )
				&& $this->status->may_send();
		}

		return $this->sending;
	}

	/**
	 * Names the page about to be sent, when it is one to name.
	 */
	public function name_page(): void {
		if ( headers_sent() || ! $this->is_sending() || ! $this->status->names_site() || ! $this->report->is_nameable( $this->status->sends_from() ) ) {
			return;
		}

		try {
			$name = bin2hex( random_bytes( 16 ) );
		} catch ( Exception $failure ) {
			return;
		}

		header( sprintf( 'Server-Timing: %s;desc="%s"', self::TIMING, $name ), false );
		$this->name = $name;
	}

	/**
	 * Takes the name back off a page that turns out to be a redirect.
	 *
	 * WordPress decides to redirect after the headers are prepared — to add a missing slash, to
	 * reach a post's proper address — and a browser that is redirected is reported on the page it
	 * lands on, not on the redirect. A name given to a page that is not reported would only ever be
	 * sent back unanswered, so it is removed; any other timing the response carries is kept.
	 *
	 * @param mixed $location Where the redirect leads.
	 * @return mixed The same address.
	 */
	public function unname_redirect( $location ) {
		// An earlier filter that cancelled the redirect leaves the page to be sent, under its name.
		if ( ! $location || '' === $this->name || headers_sent() ) {
			return $location;
		}

		$others = array();

		foreach ( headers_list() as $header ) {
			if ( 0 === stripos( $header, 'server-timing:' ) && false === strpos( $header, $this->name ) ) {
				$others[] = $header;
			}
		}

		header_remove( 'Server-Timing' );

		foreach ( $others as $header ) {
			header( $header, false );
		}

		$this->name = '';

		return $location;
	}

	/**
	 * Reports the request just answered, when it is one to report.
	 */
	public function report(): void {
		if ( ! $this->is_sending() ) {
			return;
		}

		// A site reached through Cloudflare needs Cloudflare's list, fetched once reports are being
		// sent and every week from then on, whatever became of the weekly fetch before.
		if ( '' !== Request_Headers::header( 'CF-Connecting-IP', 64 ) ) {
			Cloudflare_Ranges::schedule();
		}

		$observation = $this->report->observation(
			$this->name,
			$this->status->sends_from(),
			(int) http_response_code(),
			Request_Report::content_type_in( headers_list() )
		);

		if ( null === $observation ) {
			return;
		}

		self::finish_page();

		// The key itself is read only now, once the page has gone: one that does not open — the
		// site's secret keys changed since it was saved — stops the reports, rather than leaving
		// every page to be finished early for a report that is never sent.
		if ( '' === $this->server_key->key() ) {
			$this->status->stop( Reporting_Status::KEY_UNREADABLE );

			return;
		}

		if ( $this->status->claim() ) {
			$this->client->report( $observation );
		}
	}

	/**
	 * Hands the page to the visitor, where the server can, and readies the request to carry on
	 * without them.
	 *
	 * The request is told to carry on if the visitor goes, and any session is closed, so the
	 * visitor's next request is not kept waiting for this one to end.
	 */
	private static function finish_page(): void {
		if ( function_exists( 'fastcgi_finish_request' ) ) {
			fastcgi_finish_request();
		} elseif ( function_exists( 'litespeed_finish_request' ) ) {
			litespeed_finish_request();
		}

		ignore_user_abort( true );

		if ( PHP_SESSION_ACTIVE === session_status() ) {
			session_write_close();
		}
	}
}
