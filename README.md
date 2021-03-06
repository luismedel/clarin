![Image](./logo-w480.png)

Clarin is a simple, ~~zero-dependency~~, dogmatic static website generator written in C#.

Clarin takes a directory with content files and templates and renders them into a static HTML website. No more. No less.

## Overview

Clarin isn’t flexible, nor optimized for speed, ease of use or configurability. It was modelled to satisfy [*my*](https://luismedel.com) personal needs and preferences (which could be yours' too if you're lucky enough!)

* I don't want to mess with runtimes, ~~containers~~, databases nor ~~dependency trees~~.
* I don't want to study a new templating system, a npm/yarn/bundler/whatever based build system, how to integrate with LESS...and whatnot, only to know if a generator is suited for me.
* I only want to take a bunch of .html or .md files and get them converted to a site, with minimal effort and complexity *from the start*.

## Usage

```sh
Usage:
clarin <command> [--local] [<path>]

Commands:
build       generates the site in <path>/output
watch       watches for changes and builds the site continuously
init        inits a new site
add         adds a new empty entry in <path>/content
version     prints the version number of Clarin

--local     overrides the value in site.ini and sets the site url
            to the current site local path

<path> defaults to current directory if not specified.
```

### Create a new site

```sh
# cd /home/johndoe/

# clarin init my-site

# cd /home/johndoe/my-site
```

### Edit the site config

```sh
# cat /home/johndoe/my-site/site.ini
title       = my new site
description = my new site description
author      = author name

; Root url for your site. Can be a local path too
url         = http://127.0.0.1/

; Defines how Clarin prints the dates when using the 'date' filter
dateFormat  = yyyy-MM-dd
```

Note the lack of quotes, double quotes and any other string delimiter for values. You can use them if you want, but they aren't needed.

### Content and non-content files

Clarin looks inside the ```content``` directory. Files with .html, .md and .xml extensions are processed as content files. Everything else (css, images and anything else) is copied as-is to the ```output``` folder preserving the directory structure.

For content files Clarin expects the metadata, if any, to be defined in the first lines, between ```---``` lines.

```
---
title: Content title
date: 20220114
category: blog
---

Your content here.
```

You can add as many metadata fields as you want. Just remember that:
* Clarin won't render content with ```draft:true```.
* The field ```category``` is needed for index tags to work (explained later).

Also, Clarin ignores any file starting with an underscore or dot, like ```_20220131-draft.md``` and ```.another-draft.md```.
If you want to ignore the full contents of a directory, place an empty file named ```.clarinignore``` in it.

### Add new content

```sh
# cd /home/johndoe/my-site
# clarin add
Added /home/johndoe/my-site/content/20220114-newentry.md

# cat content/20220114-newentry.md
---
title: new entry
date: 20220114
category: blog
draft: true
---

Your content here.
```

### Editing your content

Clarin uses a very simple and familiar syntax for your content tags:

```sh
# cat /home/johndoe/my-site/content/20220114-newentry.md
```



```markdown
---
title: new entry
date: 20220114
category: blog
draft: true
---

For this entry:
- Was written on {date} (pretty formated as {date|date}).
- Has category {category}.
- The url is {url} (automatically generated).
- The slug is {slug} (automatically generated if not present in metadata).
- Is in the site {site.title}, at {site.url} (as you can see, you can use any field present in site.ini using the prefix 'site.').
- Was generated on {sys.date|date} (sys.date is always the current date and time).
```

Clarin expects some values to exists in order to work properly (but tries it's best to run ok if not present):

* slug: used to generate the output filename. If not present, Clarin tries to get it from the source filename.
* category: used for content filtering in the index tags.
* date: used for ordering in the index tags.
* draft: set to ```true``` if you want the entry to be ignored by Clarin.

### Tag filters

Clarin supports the use of filters in the form ```{value|filter}``` to modify how your content tags will render:

```
upper     Prints the value in UPPERCASE
lower     Prints the value in lowercase
date      Prints the value using site.dateFormat (as defined in site.ini)
rfc822    Prints the value as a RFC822 date (for atom feeds, basically)
```

Any other unknown filter will be trated as a date formatting pattern:

* yyyyMMdd: Prints the value as ```yyyyMMdd```.
* yyyy-MM-dd: Prints the value as ```yyyy-MM-dd```.
* ...and so on.

### About date values

When using any of the date filters (```date```, ```rfc822``` and ```yyyyMMdd```) Clarin tries to parse the input value using one of the following formats:

* yyyyMMdd
* yyyy-MM-dd
* yyyy.MM.dd
* yyyy/MM/dd

### Content urls

You can insert the url of a content in two ways:

* Using the form ```{site.url}filename.html```
* Using the sorthand ```{#<slug>}``` where ```<slug>``` is the slug of the content you want to get the url of.

### Create an index (aka 'index tags')

You can insert an index for your content using the following syntax in any content page:

```html
{%index|<category>|<pattern>%}
```

or

```html
{%index|<category>(<count>)|<pattern>%}
```

For example, to generate an index for all your entries with category 'blog' in your index.html file:

```html
<p>Welcome to my blog!</p>
<ul>
	{%index|blog|
	<li>
		<a href="{url}">{title}</a>
	</li>
	%}
</ul>
```

And the same for Markdown content:

```markdown
Welcome to my blog!
	{%index|blog|
	- [{title}]({url}) %}
```

Another example of how you can generate an atom.xml file for the last 10 blog entries:

```xml
<feed xmlns="http://www.w3.org/2005/Atom">
	<title>{site.url} feed</title>
	<link href="{url}" rel="self"/>
	<link href="{site.url}" rel="alternate"/>
	<id>{site.url}</id>
	<updated>{sys.date|RFC822}</updated>
	{%index|blog(10)|
	<entry>
		<id>{url}</id>
		<link href="{url}"/>
		<title>{title}</title>
		<author><name>{site.author}</name></author>
		<published>{date|RFC822}</published>
		<summary>{excerpt}</summary>
	</entry>
	%}
</feed>
```

Of course, you can insert as many indices as you need:

```markdown
Welcome to my blog!

Here you have my personal posts:
	{%index|personal|
	- [{title}]({url}) %}

Here you have my programming posts:
	{%index|programming|
	- [{title}]({url}) %}
```

### Templates

You can place your templates under the 'templates' directory.

Template files use the same exact syntax as the rest of the content files. The only difference is that Clarin will insert the current page contents where you place the ```{content}``` tag.

So, having this template in ```<site>/templates/page.html```...

```html
<!DOCTYPE html>
<html>
	<head>
		<meta content="text/html; charset=utf-8" http-equiv="content-type">
		<title>{title}</title>
		<meta content="width=device-width, initial-scale=1" name="viewport">
	</head>
	<body>
		<h1>{title}</h1>
		{content}
	</body>
</html>
```

...and this content in ```<site>/content/20220114-hello.html```...

```markdown
---
title: Hello!
date: 20220114
category: blog
template: page.html
---

<p>Hello World!</p>
```

...you'll get  the following output in ```<site>/output/hello.html```.

```html
<!DOCTYPE html>
<html>
	<head>
		<meta content="text/html; charset=utf-8" http-equiv="content-type">
		<title>Hello!</title>
		<meta content="width=device-width, initial-scale=1" name="viewport">
	</head>
	<body>
		<h1>Hello!</h1>

		<p>Hello World!</p>
	</body>
</html>
```

Maybe you want to reuse some snippet in several templates. Supose you have this snippet in ```<path>/templates/head.html```:

```html
<head>
	<meta content="text/html; charset=utf-8" http-equiv="content-type">
	<title>{title}</title>
	<meta content="width=device-width, initial-scale=1" name="viewport">
</head>
```

You can include the snippet in other templates using the ```{%inc|template%}``` tag:

```html
<!DOCTYPE html>
<html>
	{%inc|head.html%}
	<body>
		<h1>{title}</h1>
		{content}
	</body>
</html>
```

### Build your site

Use the ```build``` command.

```sh
# cd /home/johndoe/my-site
# clarin build
```

### Example site

See the example site included.

### Docker image

There's a [Docker image](https://hub.docker.com/r/luismedel/clarin) you can run to use Clarin without headaches.

Suppose you have your site in ```/home/johndoe/site```. Simply run:

```sh
# docker run -v /home/johndoe/site:/site luismedel/clarin <command>
```

...where ```<command>``` is any clarin command (like ```build```, for example)

