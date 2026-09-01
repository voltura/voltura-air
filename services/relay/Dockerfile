FROM node:24.20.0-alpine@sha256:e67514e5d0f6c46656005e1b693b2ec9d52e80b641307de684d4a015ba7a4eaf AS build
WORKDIR /app
COPY package*.json ./
COPY services/relay/package.json services/relay/package.json
RUN npm install --global npm@12.0.2 \
    && npm ci --workspace @voltura-air/relay --include-workspace-root=false
COPY services/relay services/relay
CMD ["npm", "run", "start", "--workspace", "@voltura-air/relay"]
