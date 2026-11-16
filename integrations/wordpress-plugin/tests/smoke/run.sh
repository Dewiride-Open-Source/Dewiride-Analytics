#!/usr/bin/env bash
# Installs the plugin in a throwaway WordPress, checks what visitors and administrators receive, and
# checks what the site's own server reports to a stand-in for Dewiride Analytics.
#
#   PLUGIN_DIR=/absolute/path/to/dewiride-analytics bash tests/smoke/run.sh floor|newest
#
# floor   The oldest WordPress the plugin supports, on PHP 7.4, with a classic theme that has no
#         HTML5 script support, so WordPress prints `type="text/javascript"` and boolean attributes
#         as `name="name"`. No official image carries WordPress 6.4, so the newest 6.4 release is
#         looked up and installed over the newest PHP 7.4 image.
# newest  The newest WordPress, release candidates included, on the newest PHP, with the default
#         block theme.
#
# PLUGIN_DIR is the folder holding the plugin as it ships: the extracted dewiride-analytics/ of the
# zip. On Windows, give it as a Windows path (`pwd -W` in Git Bash). Needs Docker and nothing
# else, exits non-zero when any assertion fails, and removes its containers and volumes whatever
# happens.

set -euo pipefail

# Git Bash on Windows rewrites arguments that look like paths before Docker sees them. The paths
# below are paths inside the containers and must reach Docker exactly as written.
export MSYS_NO_PATHCONV=1

readonly ADDRESS='https://analytics.example.test'
readonly SITE_ID='5d0c7a1e-8b2f-4c3d-9e4a-6f7b8c9d0e1f'
readonly HOME_URL='http://wordpress'
# Shaped like a server key, and plainly not one: a single repeated character.
SERVER_KEY="dwk_$(printf 'k%.0s' $(seq 1 43))"
readonly SERVER_KEY
readonly BROWSER='Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/142.0.0.0 Safari/537.36'

case "${1:-}" in
  floor)
    export SMOKE_WORDPRESS_IMAGE='wordpress:php7.4-apache'
    export SMOKE_CLI_IMAGE='wordpress:cli-php7.4'
    markup='legacy'
    ;;
  newest)
    export SMOKE_WORDPRESS_IMAGE='wordpress:beta-7.2-php8.5-apache'
    export SMOKE_CLI_IMAGE='wordpress:cli-2.12.0-php8.5'
    markup='html5'
    ;;
  *)
    echo "usage: PLUGIN_DIR=<folder holding the plugin> bash $0 floor|newest" >&2
    exit 64
    ;;
esac

if [ -z "${PLUGIN_DIR:-}" ]; then
  echo 'PLUGIN_DIR must name the folder holding the plugin.' >&2
  exit 64
fi

mode="$1"
export PLUGIN_DIR SITE_ID
export SMOKE_SITE_ID="$SITE_ID"
export SMOKE_DATABASE_IMAGE='mariadb:12.3'
export SMOKE_NODE_IMAGE='node:24-alpine'
SMOKE_DATABASE_PASSWORD="$(od -An -N16 -tx1 /dev/urandom | tr -d ' \n')"
export SMOKE_DATABASE_PASSWORD

cd "$(dirname "${BASH_SOURCE[0]}")"

project="dewiride-analytics-smoke-${mode}-$$"
work="$(mktemp -d)"
failures=0

compose() {
  docker compose --project-name "$project" --file compose.yaml "$@"
}

cli() {
  compose exec -T cli "$@"
}

cleanup() {
  compose down --volumes --remove-orphans >/dev/null 2>&1 || true
  rm -rf "$work"
}

trap cleanup EXIT

# Checks a page with inspect.php inside WordPress: present, absent or judged.
check() {
  local description="$1" expect="$2" page="$3" result
  result="$(compose exec -T \
    -e SMOKE_EXPECT="$expect" \
    -e SMOKE_ADDRESS="$ADDRESS" \
    -e SMOKE_SITE_ID="$SITE_ID" \
    -e SMOKE_PAGE="$HOME_URL/" \
    -e SMOKE_MARKUP="$markup" \
    cli wp eval-file /smoke/inspect.php <"$page")"
  verdict "$description" "$result"
}

# Makes one request as a visitor and checks what came back and what was reported because of it:
# named, reported or ignored. Headers are a JSON object; the user agent is a crawler's unless given.
# A timing given last must still be in the page's Server-Timing header.
# The request is made from the stand-in collector's container: WordPress's and WP-CLI's addresses
# are the site's own, whose requests the plugin never reports.
report() {
  local description="$1" expect="$2" method="$3" path="$4" status="$5" headers="${6:-}" agent="${7:-}" body="${8:-}" keeps="${9:-}" result
  result="$(compose exec -T \
    -e SMOKE_EXPECT="$expect" \
    -e SMOKE_METHOD="$method" \
    -e SMOKE_PATH="$path" \
    -e SMOKE_STATUS="$status" \
    -e SMOKE_HEADERS="$headers" \
    -e SMOKE_AGENT="$agent" \
    -e SMOKE_BODY="$body" \
    -e SMOKE_KEEPS="$keeps" \
    -e SMOKE_KEY="$SERVER_KEY" \
    collector node /smoke/visit.mjs)"
  verdict "$description" "$result"
}

# Saves the settings as the settings page does, with the boxes given ticked and the key pasted.
save_server_settings() {
  compose exec -T \
    -e SMOKE_ADDRESS="$ADDRESS" \
    -e SMOKE_SITE_ID="$SITE_ID" \
    -e SMOKE_KEY="$SERVER_KEY" \
    -e SMOKE_TICKED="$1" \
    cli wp eval '
      ( new \Dewiride\Analytics\WordPress\Settings() )->register_setting();
      $ticked = array_fill_keys( array_filter( explode( ",", getenv( "SMOKE_TICKED" ) ) ), "1" );
      update_option(
        \Dewiride\Analytics\WordPress\Settings::OPTION,
        array( "address" => getenv( "SMOKE_ADDRESS" ), "site_id" => getenv( "SMOKE_SITE_ID" ), "server_key" => getenv( "SMOKE_KEY" ) ) + $ticked
      );
    ' >/dev/null
}

# Records one assertion: passed when the result is exactly "ok".
verdict() {
  if [ "$2" = 'ok' ]; then
    echo "passed: $1"
  else
    printf 'FAILED: %s\n%s\n' "$1" "$2" >&2
    failures=$((failures + 1))
  fi
}

echo "Starting WordPress ($mode: $SMOKE_WORDPRESS_IMAGE, $SMOKE_CLI_IMAGE, $SMOKE_DATABASE_IMAGE)"
compose up --detach --wait --quiet-pull

# The WordPress image copies WordPress into its volume and writes wp-config.php as it starts.
for _ in $(seq 1 90); do
  if cli test -f /var/www/html/wp-config.php; then
    break
  fi
  sleep 2
done

admin_password="$(od -An -N16 -tx1 /dev/urandom | tr -d ' \n')"

cli wp core install \
  --url="$HOME_URL" \
  --title='Dewiride Analytics smoke test' \
  --admin_user='administrator' \
  --admin_password="$admin_password" \
  --admin_email='administrator@example.test' \
  --skip-email

if [ "$mode" = 'floor' ]; then
  floor="$(cli php -r '
    $versions = json_decode( file_get_contents( "https://api.wordpress.org/core/stable-check/1.0/" ), true );
    $floor    = array_filter( array_keys( $versions ), function ( $version ) {
      return 1 === preg_match( "/^6\\.4(\\.[0-9]+)?$/", $version );
    } );
    usort( $floor, "version_compare" );
    echo end( $floor );
  ')"
  echo "Installing WordPress $floor, the newest 6.4 release"
  cli wp core update --version="$floor" --force
  cli wp core update-db
  cli wp theme activate dewiride-smoke
fi

echo "WordPress $(cli wp core version) on PHP $(cli wp eval 'echo PHP_VERSION;'), theme $(cli wp eval 'echo get_stylesheet();')"

cli wp plugin activate dewiride-analytics

# The settings saved for the first time, the way the settings page saves them: through
# update_option(), with the setting registered with its default as the dashboard's admin_init
# registers it. A command never reaches the dashboard, so it registers the setting through the
# plugin's own method. Activation stored that default, so WordPress adds the option instead of
# updating it, and the check of the tag has to become due at once all the same — not stay a day
# away, as activation scheduled it.
saved="$(compose exec -T \
  -e SMOKE_ADDRESS="$ADDRESS" \
  -e SMOKE_SITE_ID="$SITE_ID" \
  cli wp eval '
    $event  = \Dewiride\Analytics\WordPress\Tag_Check::EVENT;
    $option = \Dewiride\Analytics\WordPress\Settings::OPTION;
    $before = wp_next_scheduled( $event );
    ( new \Dewiride\Analytics\WordPress\Settings() )->register_setting();
    update_option( $option, array( "address" => getenv( "SMOKE_ADDRESS" ), "site_id" => getenv( "SMOKE_SITE_ID" ) ) );
    $saved = get_option( $option );
    $after = wp_next_scheduled( $event );
    echo false !== $before && $before > time() && getenv( "SMOKE_ADDRESS" ) === ( $saved["address"] ?? null ) && false !== $after && $after <= time()
      ? "ok"
      : wp_json_encode( array( "check before" => $before, "saved" => $saved, "check after" => $after, "now" => time() ) );
  ')"
verdict "the first save of the settings makes a check of the tag due at once" "$saved"

# A visitor's request, made from inside the network as an anonymous browser would.
cli php -r 'echo file_get_contents( "http://wordpress/" );' >"$work/visitor.html"
check "a visitor receives the tag and the no-script image" present "$work/visitor.html"

# The same page parts rendered by WordPress, for nobody and then for the administrator.
cli wp eval --url="$HOME_URL/" 'wp_head(); wp_body_open(); wp_footer();' >"$work/anonymous.html"
check "the page parts rendered for nobody carry the tag and the image" present "$work/anonymous.html"
cli wp eval --url="$HOME_URL/" 'wp_set_current_user( 1 ); wp_head(); wp_body_open(); wp_footer();' >"$work/administrator.html"
check "the page parts rendered for the administrator carry neither" absent "$work/administrator.html"

check "the tag check judges the page and its altered copies correctly" judged "$work/visitor.html"

# The tag check from end to end: WordPress fetches its own home page and keeps the outcome.
kept="$(cli wp eval '
  ( new \Dewiride\Analytics\WordPress\Tag_Check( new \Dewiride\Analytics\WordPress\Settings() ) )->run();
  $kept = get_option( \Dewiride\Analytics\WordPress\Tag_Check::OPTION );
  echo is_array( $kept ) && "intact" === $kept["outcome"] && is_int( $kept["checked_at"] ) ? "ok" : wp_json_encode( $kept );
')"
verdict "the daily check fetches the home page and keeps 'intact'" "$kept"

# The site's own server reporting what reaches WordPress, to the stand-in collector. Permalinks are
# made pretty first, so that WordPress — not the web server — answers the favicon, the REST API and
# missing files, as it does on a real site.
echo "Reporting what reaches WordPress"
cli wp rewrite structure '/%postname%/' --hard >/dev/null

save_server_settings 'report_server,report_feeds'
key_kept="$(cli wp eval '
  $settings = get_option( \Dewiride\Analytics\WordPress\Settings::OPTION );
  $sealed   = get_option( \Dewiride\Analytics\WordPress\Server_Key::OPTION );
  $key      = ( new \Dewiride\Analytics\WordPress\Server_Key() )->key();
  echo "kkkk" === ( $settings["key_ending"] ?? null ) && ! isset( $settings["server_key"] ) && is_string( $sealed ) && false === strpos( $sealed, "dwk_" ) && 0 === strpos( $key, "dwk_" )
    ? "ok"
    : wp_json_encode( array( "settings" => $settings, "sealed" => is_string( $sealed ) ) );
')"
verdict "the server key is saved sealed, and only its ending kept in the settings" "$key_kept"

# Both images run PHP inside Apache, which cannot finish a page before the request ends, so the
# server's reports stay off until holding robots' pages open is accepted.
report "on a server that holds pages open, nothing is reported until that is accepted" ignored GET / 200
save_server_settings 'report_server,report_unfinished,report_feeds'

# Saving the key again starts the reports afresh, so nothing has yet shown that the installation
# joins names safely by naming the website in an answer: a browser's page goes unnamed until one
# does, and this report's answer is the first.
report "a browser is not named until the installation names the website" reported GET /hello-world/ 200 \
  '{"Sec-Fetch-Dest":"document","Sec-Fetch-Mode":"navigate"}' "$BROWSER"

connected="$(cli wp eval '
  $settings = new \Dewiride\Analytics\WordPress\Settings();
  $key      = new \Dewiride\Analytics\WordPress\Server_Key();
  $status   = new \Dewiride\Analytics\WordPress\Reporting_Status( $settings );
  $check    = new \Dewiride\Analytics\WordPress\Connection_Check( $settings, $key, new \Dewiride\Analytics\WordPress\Collector_Client( $settings, $key, $status ), $status, new \Dewiride\Analytics\WordPress\Tag_Check( $settings ) );
  $result   = $check->run();
  echo "connected" === $result["outcome"] && ! $result["older"] && ( new \Dewiride\Analytics\WordPress\Reporting_Status( $settings ) )->names_site()
    ? "ok"
    : wp_json_encode( $result );
')"
verdict "Check connection finds the installation in order, and it names the website" "$connected"

report "a crawler's page is reported, unnamed" reported GET /hello-world/ 200
report "a browser opening a page is sent it under a name, and reported with it" named GET /hello-world/ 200 \
  '{"Sec-Fetch-Dest":"document","Sec-Fetch-Mode":"navigate","Sec-CH-UA-Mobile":"?0"}' "$BROWSER"
report "a browser prefetching a page is neither named nor reported" ignored GET /hello-world/ 200 \
  '{"Sec-Fetch-Dest":"document","Sec-Purpose":"prefetch"}' "$BROWSER"
report "a redirected browser is left to the page it lands on" ignored GET /hello-world 301 \
  '{"Sec-Fetch-Dest":"document","Sec-Fetch-Mode":"navigate"}' "$BROWSER"
report "a redirect loses the page's name and keeps another plugin's timing" ignored GET /hello-world 301 \
  '{"Sec-Fetch-Dest":"document","Sec-Fetch-Mode":"navigate","X-Smoke-Timing":"1"}' "$BROWSER" '' 'other;dur=1'
# Browsers send no Sec-Fetch-* headers to a site served over http, as this one is, and ask to be
# upgraded to https on every navigation instead.
report "a browser redirected on a site served over http is left to the page it lands on" ignored GET /hello-world 301 \
  '{"Upgrade-Insecure-Requests":"1"}' "$BROWSER"
report "a redirected crawler is reported" reported GET /hello-world 301
report "a HEAD request that was answered is left out" ignored HEAD /hello-world/ 200
report "a feed is reported" reported GET /feed/ 200
report "a feed reader told the feed has not changed is reported" reported GET /feed/ 304 \
  '{"If-Modified-Since":"Wed, 01 Jan 2098 00:00:00 GMT"}'
report "WordPress's own sitemap is reported" reported GET /wp-sitemap.xml 200
report "the REST API is not reported" ignored GET /wp-json/ 200
report "the favicon is not reported" ignored GET /favicon.ico 302
report "a browser's missing image is left out" ignored GET /wp-content/uploads/missing.png 404 '{"Sec-Fetch-Dest":"image"}' "$BROWSER"
report "a probe is reported whatever it calls itself" reported GET /.env 404 '{"Sec-Fetch-Dest":"image","Sec-Purpose":"prefetch"}'
report "a probe asking for an embed of itself is reported" reported GET '/.env?embed=1' 404
report "a page the site refused is reported, even asked for as a preview" reported GET \
  '/hello-world/?smoke_refuse=1&preview=1' 403
report "the sign-in page is not reported unless chosen" ignored GET /wp-login.php 200

save_server_settings 'report_server,report_unfinished,report_feeds,report_sign_in'
signed_in="$(printf 'log=administrator&pwd=%s&testcookie=1' "$admin_password")"
cookie='{"Content-Type":"application/x-www-form-urlencoded","Cookie":"wordpress_test_cookie=WP%20Cookie%20check"}'
report "a program asking for the sign-in page is reported, once chosen" reported GET /wp-login.php 200
report "a program's sign-in that WordPress refused is reported" reported POST /wp-login.php 200 "$cookie" '' \
  'log=administrator&pwd=not-the-password&testcookie=1'
report "a sign-in that succeeded is not reported" ignored POST /wp-login.php 302 "$cookie" '' "$signed_in"
report "somebody signing in in a browser is not reported" ignored POST /wp-login.php 200 \
  '{"Content-Type":"application/x-www-form-urlencoded","Cookie":"wordpress_test_cookie=WP%20Cookie%20check","Sec-Fetch-Dest":"document","Sec-Fetch-Mode":"navigate"}' \
  "$BROWSER" 'log=administrator&pwd=not-the-password&testcookie=1'
report "XML-RPC is reported, once chosen" reported GET /xmlrpc.php 405

# A security plugin's firewall can refuse a request before WordPress has parsed it: while plugins
# are still loading, before WordPress's own functions for who is signed in exist, or as WordPress
# starts. What it refused is reported all the same, as a page or a sign-in, and the REST API still
# is not.
report "a probe a firewall refused while plugins were loading is reported" reported GET '/.env?smoke_firewall=plugin_loaded' 403
report "a probe a firewall refused as WordPress started is reported" reported GET '/.env?smoke_firewall=init' 403
report "a sign-in a firewall refused as WordPress started is reported" reported POST '/wp-login.php?smoke_firewall=init' 403 \
  "$cookie" '' 'log=administrator&pwd=not-the-password&testcookie=1'
report "the REST API refused by a firewall is not reported" ignored GET '/wp-json/?smoke_firewall=init' 403

# The plugin's own requests carry a header only the site can write, for the page they ask for. A
# crawler's request carrying it is the site's own and is left out; one carrying anything else is
# reported.
marker="$(cli wp eval 'echo \Dewiride\Analytics\WordPress\Own_Request::marker( home_url( "/hello-world/" ) );')"
report "a request carrying the site's own mark is not reported" ignored GET /hello-world/ 200 \
  "{\"X-Dewiride-Analytics-Own\":\"$marker\"}"
report "a request carrying a mark the site did not write is reported" reported GET /hello-world/ 200 \
  '{"X-Dewiride-Analytics-Own":"forged"}'

own="$(compose exec -T -e SMOKE_KEY="$SERVER_KEY" cli wp eval '
  $count = static function (): int {
    return is_readable( "/reports/requests.jsonl" ) ? count( (array) file( "/reports/requests.jsonl", FILE_SKIP_EMPTY_LINES ) ) : 0;
  };
  $before = $count();
  ( new \Dewiride\Analytics\WordPress\Tag_Check( new \Dewiride\Analytics\WordPress\Settings() ) )->run();
  echo $before === $count() ? "ok" : "the site reported its own request for its home page";
')"
verdict "the site's own request for its home page is not reported" "$own"

if [ "$failures" -gt 0 ]; then
  echo "$failures assertion(s) failed ($mode)." >&2
  exit 1
fi

echo "Every assertion passed ($mode)."
