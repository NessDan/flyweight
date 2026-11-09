// Sends http:// and www. addresses to https://getflyweight.com, keeping the path and query string.
// Its routes (wrangler.jsonc) match only those addresses, so the site itself runs no code.
const HOME = "getflyweight.com";

export default {
  fetch(request) {
    const url = new URL(request.url);

    // The routes should never send the real address here. If they ever do, serve it rather than loop.
    if (url.protocol === "https:" && url.hostname === HOME) return fetch(request);

    url.protocol = "https:";
    url.hostname = HOME;
    url.port = "";
    return Response.redirect(url.href, 301);
  },
};
