<?php
/**
 * Prints any page of the smoke theme: the titles of the posts it shows.
 *
 * @package Dewiride_Analytics
 */

get_header();

while ( have_posts() ) {
	the_post();
	the_title( '<h1>', '</h1>' );
}

get_footer();
