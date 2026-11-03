/**
 * Navigation inside the site stays in the SPA. The generated static pages exist for search
 * engines; opening one from a result is a normal entry point, but moving between docs should not
 * pay for a full page load each time.
 */
export const docsHref = (slug: string) => `#/docs/${slug}`;

export const homeHref = () => "#/";
