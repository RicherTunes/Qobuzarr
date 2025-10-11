CI requires a repo-scoped PAT for private submodules.
Create a secret SUBMODULES_TOKEN with `repo` scope (or fine-grained access to `RicherTunes/Lidarr.Plugin.Common`) so actions/checkout can clone submodules.
