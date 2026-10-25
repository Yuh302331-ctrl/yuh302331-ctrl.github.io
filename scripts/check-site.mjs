import { readFile } from "node:fs/promises";

const index = await readFile("index.html", "utf8");
const notFound = await readFile("404.html", "utf8");
const sitemap = await readFile("sitemap.xml", "utf8");
const data = JSON.parse(await readFile("data/github.json", "utf8"));

const inlineScripts = [...index.matchAll(/<script>([\s\S]*?)<\/script>/g)];
if (inlineScripts.length !== 1) {
  throw new Error(`Expected one inline script, found ${inlineScripts.length}.`);
}
new Function(inlineScripts[0][1]);

if (!data.user || !Array.isArray(data.repos)) {
  throw new Error("data/github.json is missing user or repos.");
}
if (index.includes("api.github.com")) {
  throw new Error("index.html must not call api.github.com at runtime.");
}
if (!notFound.includes("noindex,nofollow")) {
  throw new Error("404.html must be noindex,nofollow.");
}
if (!sitemap.includes("https://yuh302331-ctrl.github.io/")) {
  throw new Error("sitemap.xml is missing the canonical homepage URL.");
}

console.log("Site checks passed.");
