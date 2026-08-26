FROM node:24.19.0-alpine@sha256:d32cdf619f63fe0471182d08996dd516c6275bb5fd31ae06e55a570bd9e1ad43 AS build
WORKDIR /app
COPY package*.json ./
COPY services/relay/package.json services/relay/package.json
RUN npm install --global npm@11.19.0 \
    && npm ci --workspace @voltura-air/relay --include-workspace-root=false
COPY services/relay services/relay
CMD ["npm", "run", "start", "--workspace", "@voltura-air/relay"]
