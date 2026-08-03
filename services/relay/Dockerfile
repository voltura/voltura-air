FROM node:24-alpine AS build
WORKDIR /app
COPY package*.json ./
COPY services/relay/package.json services/relay/package.json
RUN npm install --workspace @voltura-air/relay --include-workspace-root=false
COPY services/relay services/relay
CMD ["npm", "run", "start", "--workspace", "@voltura-air/relay"]
