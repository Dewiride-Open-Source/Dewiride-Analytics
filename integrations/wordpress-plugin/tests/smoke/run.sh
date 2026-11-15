#!/usr/bin/env bash
# Installs the plugin in a throwaway WordPress and checks what visitors and administrators receive.
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
export PLUGIN_DIR
export SMOKE_DATABASE_IMAGE='mariadb:12.3'
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

cli wp core install \
  --url="$HOME_URL" \
  --title='Dewiride Analytics smoke test' \
  --admin_user='administrator' \
  --admin_password="$(od -An -N16 -tx1 /dev/urandom | tr -d ' \n')" \
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

if [ "$failures" -gt 0 ]; then
  echo "$failures assertion(s) failed ($mode)." >&2
  exit 1
fi

echo "Every assertion passed ($mode)."
