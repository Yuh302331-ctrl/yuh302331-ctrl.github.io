import { mkdir, readFile, writeFile } from "node:fs/promises";

const login = process.env.GITHUB_REPOSITORY_OWNER || "Yuh302331-ctrl";
const token = process.env.GH_TOKEN || process.env.GITHUB_TOKEN || "";
const headers = {
  Accept: "application/vnd.github+json",
  "User-Agent": `${login}-homepage-data`,
  "X-GitHub-Api-Version": "2022-11-28",
  ...(token ? { Authorization: `Bearer ${token}` } : {}),
};

async function getJSON(path) {
  const response = await fetch(`https://api.github.com${path}`, { headers });
  if (!response.ok) {
    throw new Error(`${path} returned HTTP ${response.status}`);
  }
  return response.json();
}

const [user, repos] = await Promise.all([
  getJSON(`/users/${encodeURIComponent(login)}`),
  getJSON(`/users/${encodeURIComponent(login)}/repos?per_page=100&sort=pushed`),
]);

const data = {
  generated_at: new Date().toISOString(),
  user: {
    login: user.login,
    public_repos: user.public_repos,
    followers: user.followers,
    created_at: user.created_at,
  },
  repos: repos.map((repo) => ({
    name: repo.name,
    description: repo.description,
    language: repo.language,
    html_url: repo.html_url,
    stargazers_count: repo.stargazers_count,
    pushed_at: repo.pushed_at,
    archived: repo.archived,
    fork: repo.fork,
  })),
};

let previous = null;
try {
  previous = JSON.parse(await readFile("data/github.json", "utf8"));
} catch {}

if (
  previous &&
  JSON.stringify(previous.user) === JSON.stringify(data.user) &&
  JSON.stringify(previous.repos) === JSON.stringify(data.repos)
) {
  console.log("GitHub data is unchanged.");
  process.exit(0);
}

await mkdir("data", { recursive: true });
await writeFile("data/github.json", `${JSON.stringify(data, null, 2)}\n`, "utf8");
