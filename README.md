![Image](./logo-w480.png)

Clarin is a simple, zero-dependency, dogmatic static website generator written as a single C# source file.

Clarin takes a directory with content files and templates and renders them into a static HTML website. No more. No less.

## Overview

Clarin isn’t flexible, nor optimized for speed, ease of use or configurability. It was modelled to satisfy [*my*](https://luismedel.com) personal needs and preferences (which could be yours' too if you're lucky enough!)

* I don't want to mess with runtimes, containers, databases nor a dependency tree.
* I don't want to study a new templating system, a npm/yarn/bundler/whatever based build system, how to integrate with LESS...and whatnot, only to know if a generator is suited for me.
* I only want to take a bunch of .html or .md files and get them converted to a site, with minimal effort and complexity *from the start*.

## Usage

```sh
Usage:
  clarin <command> [<path>]

Commands:
  build       generates the site in <path>/output
  init        inits a new site
  add         adds a new empty entry in <path>/content
  version     prints the version number of Clarin
```

### Create a new site

```sh
# cd /home/luis/

# clarin init my-site

# cd /home/luis/my-site
```

### Edit the site config

```sh
# cat /home/luis/my-site/site.ini
title       = my new site
description = my new site description
author      = author name
; Root url for your site
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

### Add new content

```sh
# cd /home/luis/my-site
# clarin add
Added /home/luis/my-site/content/20220114-newentry.md

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
# cat /home/luis/my-site/content/20220114-newentry.md
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
``````

Clarin expects some values to exists in order to work properly (but tries it's best to run ok if not present):

- ```slug```: used to generate the output filename. If not present, Clarin tries to get it from the source filename.
- ```category```: used for content filtering in the index tags.
- ```date```: used for ordering in the index tags.
- ```draft```: set to ```true``` if you want the entry to be ignored by Clarin.

### Tag filters

Clarin supports the use of filters in the form ```{value|filter}``` to modify how your content tags will render:

```
upper     Prints the value in UPPERCASE
lower     Prints the value in lowercase
date      Prints the value using site.dateFormat (as defined in site.ini)
rfc822    Prints the value as a RFC822 date (for atom feeds, basically)
```

Any other unknown filter will be trated as a date formatting pattern:

- ```yyyyMMdd```: Prints the value as yyyyMMdd.
- ```yyyy-MM-dd```: Prints the value as yyyy-MM-dd.
- ...and so on.

### About date values

When using any of the date filters (```date```, ```rfc822``` and ```yyyyMMdd```) Clarin tries to parse the input value using one of the following formats:

* ```yyyyMMdd```
* ```yyyy-MM-dd```
* ```yyyy.MM.dd```
* ```yyyy/MM/dd```

### Create an index (aka 'index tags')

You can insert an index for your content using the following syntax in any content page:

```
{%index|<category>|<pattern>%}
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

Another example of how you can generate an atom.xml file:

```xml
<feed xmlns="http://www.w3.org/2005/Atom">
	<title>{site.url} feed</title>
	<link href="{url}" rel="self"/>
	<link href="{site.url}" rel="alternate"/>
	<id>{site.url}</id>
	<updated>{sys.date|RFC822}</updated>
	{%index|blog|
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

```html
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

### A note on Markdown rendering

Clarin uses the [Github Markdown API](https://docs.github.com/en/rest/reference/markdown) to render Markdown files. If you don't want your content to be sent to a third party service **don't use Markdown or don't use Clarin**.

By default, Github [limits](https://docs.github.com/en/enterprise-server@3.1/rest/overview/resources-in-the-rest-api#rate-limiting) unauthenticated requests to it's API to 60 per hour and authenticated ones to 5000 per hour.

You can use a [personal access token (PAT)](https://docs.github.com/es/authentication/keeping-your-account-and-data-secure/creating-a-personal-access-token) to authenticate Clarin against the Github API.

To do so, put your Github username and your PAT in the following environment variables.

```
CLARIN_GHUSER = "<your GH user>"
CLARIN_GHTOKEN = "<your PAT>"
```

Alternatively, you can put them on a ```.env``` file in your site root (the same with the ```site.ini``` file) and Clarin will load it when needed.

Remind to add that file to your .gitignore! 😀

### Build your site

Use the ```build``` command.

```sh
# cd /home/luis/my-site
# clarin build
```

You can use the ```watch``` command to monitor for changes and rebuild modified files continuously.

### Example site

See the example site included.
