FROM node:26.13.1-alpine@sha256:6bd9ab43c0ec0190ca7b2d193906cb8b7edf6e7f4afa43ddeccd6452710bffb5 AS build
WORKDIR /app
COPY package*.json ./
COPY services/relay/package.json services/relay/package.json
RUN npm install --global npm@12.2.3 \
    && npm ci --omit=dev --workspace @voltura-air/relay --include-workspace-root=false
COPY services/relay/tsconfig.json services/relay/tsconfig.json
COPY services/relay/src services/relay/src
CMD ["npm", "run", "start", "--workspace", "@voltura-air/relay"]
