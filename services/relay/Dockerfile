FROM node:24.18.1-alpine@sha256:f70403e87646dc51b45295f4b8b70cdad0b63d2297c4c9899119b03f7af7a6b3 AS build
WORKDIR /app
COPY package*.json ./
COPY services/relay/package.json services/relay/package.json
RUN npm install --global npm@11.18.0 \
    && npm ci --workspace @voltura-air/relay --include-workspace-root=false
COPY services/relay services/relay
CMD ["npm", "run", "start", "--workspace", "@voltura-air/relay"]
