# Deploying to Docker Swarm

This directory contains the Docker Swarm setup for hosting EuphratesWeb apps.
Today it runs **Traefik** (ingress/TLS) plus the **AI Teacher** service (the
"Euphrates Voice" demo). Every future app is added the same way.

> **Stack name matters.** The Traefik config references the network
> `euphrates_edge`, which Docker derives from the stack name `euphrates`.
> Deploy with exactly that name: `docker stack deploy -c deploy/stack.yml euphrates`.

## 1. Build and push the image

The app image must be reachable by every swarm node, so push it to a registry
(Docker Hub, GHCR, or a private registry). For a single-node swarm you can skip
the registry and just build locally.

```bash
# From the repo root
export REGISTRY=ghcr.io/your-org      # or your Docker Hub user
export TAG=$(git rev-parse --short HEAD)

docker build -t "$REGISTRY/euphrates/ai-teacher:$TAG" .
docker push "$REGISTRY/euphrates/ai-teacher:$TAG"
```

## 2. Initialize the swarm (first time only)

```bash
# On the machine that will be the manager:
docker swarm init

# To add more nodes (workers), run the printed join command on each:
# docker swarm join --token <token> <manager-ip>:2377
```

## 3. Create the secrets (first time only)

API keys never live in the image or the stack file — they are Docker secrets,
mounted into the container at `/run/secrets/*` and read automatically by the app
(the key-per-file config provider added in `Program.cs`).

```bash
printf '%s' "sk-your-openai-key"      | docker secret create openai_api_key -
printf '%s' "your-elevenlabs-key"     | docker secret create elevenlabs_api_key -
```

Rotate a key later with `docker secret rm` + `create`, then redeploy.

## 4. Deploy

```bash
export REGISTRY=ghcr.io/your-org
export TAG=$(git rev-parse --short HEAD)
export ACME_EMAIL="admin@euphratesweb.com"
export AI_TEACHER_HOST="ai.euphratesweb.com"   # point this DNS A record at the manager
export AI_PROVIDER="OpenAI"                     # or leave as Stub for a no-cost demo
export AI_TTS_PROVIDER="OpenAI"                 # or ElevenLabs

docker stack deploy -c deploy/stack.yml euphrates
```

Traefik will obtain a Let's Encrypt certificate automatically once DNS for
`AI_TEACHER_HOST` resolves to the swarm's public IP and ports 80/443 are open.

## 5. Verify

```bash
docker stack services euphrates      # replicas should read 1/1
docker service logs euphrates_ai-teacher --tail 50
curl -fsS https://$AI_TEACHER_HOST/healthz    # -> ok
```

## Local test without DNS / TLS

Leave `AI_TEACHER_HOST=ai.localhost` (the default) and add `127.0.0.1 ai.localhost`
to `/etc/hosts`. Traefik will serve it over HTTP on port 80. TLS via Let's Encrypt
needs a real, publicly-resolvable domain.

## Adding another app to the swarm (the repeatable pattern)

1. Containerize the app (its own `Dockerfile`), build, and push to `$REGISTRY`.
2. Copy the `ai-teacher` service block in `stack.yml` and change:
   - the **image**,
   - the router **rule** host (e.g. `voice.euphratesweb.com`),
   - the router/service **names** (must be unique),
   - the **volumes** (or drop them if the app is stateless),
   - the load-balancer **server.port** to match the app's listen port.
3. `docker stack deploy -c deploy/stack.yml euphrates` again — Swarm performs a
   rolling update and leaves everything else running.

## Notes / next hardening steps

- **`ai-teacher` is pinned to 1 replica** because it persists to local JSON files
  and local disk (`App_Data`, `wwwroot/generated`). To scale it out, move that
  state to shared storage (a database + object storage / S3) first.
- Enable and password-protect the **Traefik dashboard** before exposing it.
- For a bank/enterprise delivery, this same stack lifts into the client's VPC or
  on-prem swarm unchanged — only the secrets, domain, and registry differ.
