const pngSignature = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

export function createEmbeddedSvg(png) {
  return Buffer.from(
    `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" role="img" aria-label="Voltura Air"><image width="512" height="512" href="data:image/png;base64,${png.toString("base64")}"/></svg>\n`,
  );
}

export function createIco(images) {
  const headerSize = 6;
  const entrySize = 16;
  const directory = Buffer.alloc(headerSize + images.length * entrySize);
  directory.writeUInt16LE(0, 0);
  directory.writeUInt16LE(1, 2);
  directory.writeUInt16LE(images.length, 4);

  let imageOffset = directory.length;
  images.forEach(({ size, data }, index) => {
    const entryOffset = headerSize + index * entrySize;
    directory.writeUInt8(size === 256 ? 0 : size, entryOffset);
    directory.writeUInt8(size === 256 ? 0 : size, entryOffset + 1);
    directory.writeUInt8(0, entryOffset + 2);
    directory.writeUInt8(0, entryOffset + 3);
    directory.writeUInt16LE(1, entryOffset + 4);
    directory.writeUInt16LE(32, entryOffset + 6);
    directory.writeUInt32LE(data.length, entryOffset + 8);
    directory.writeUInt32LE(imageOffset, entryOffset + 12);
    imageOffset += data.length;
  });

  return Buffer.concat([directory, ...images.map(({ data }) => data)]);
}

export function createDib(rgba, size) {
  assertRgbaLength(rgba, size, size, "ICO bitmap");

  const bitmapHeader = Buffer.alloc(40);
  bitmapHeader.writeUInt32LE(40, 0);
  bitmapHeader.writeInt32LE(size, 4);
  bitmapHeader.writeInt32LE(size * 2, 8);
  bitmapHeader.writeUInt16LE(1, 12);
  bitmapHeader.writeUInt16LE(32, 14);
  bitmapHeader.writeUInt32LE(0, 16);
  bitmapHeader.writeUInt32LE(size * size * 4, 20);

  const xorBitmap = Buffer.alloc(size * size * 4);
  for (let sourceY = 0; sourceY < size; sourceY += 1) {
    const targetY = size - sourceY - 1;
    for (let x = 0; x < size; x += 1) {
      const sourceOffset = (sourceY * size + x) * 4;
      const targetOffset = (targetY * size + x) * 4;
      xorBitmap[targetOffset] = rgba[sourceOffset + 2];
      xorBitmap[targetOffset + 1] = rgba[sourceOffset + 1];
      xorBitmap[targetOffset + 2] = rgba[sourceOffset];
      xorBitmap[targetOffset + 3] = rgba[sourceOffset + 3];
    }
  }

  const andRowSize = Math.ceil(size / 32) * 4;
  return Buffer.concat([bitmapHeader, xorBitmap, Buffer.alloc(andRowSize * size)]);
}

export function createBmp24(rgba, width, height) {
  assertRgbaLength(rgba, width, height, "BMP bitmap");

  const rowSize = Math.ceil((width * 3) / 4) * 4;
  const pixelBytes = rowSize * height;
  const header = Buffer.alloc(54);
  header.write("BM", 0, "ascii");
  header.writeUInt32LE(header.length + pixelBytes, 2);
  header.writeUInt32LE(header.length, 10);
  header.writeUInt32LE(40, 14);
  header.writeInt32LE(width, 18);
  header.writeInt32LE(height, 22);
  header.writeUInt16LE(1, 26);
  header.writeUInt16LE(24, 28);
  header.writeUInt32LE(pixelBytes, 34);
  header.writeInt32LE(3780, 38);
  header.writeInt32LE(3780, 42);

  const pixels = Buffer.alloc(pixelBytes);
  for (let sourceY = 0; sourceY < height; sourceY += 1) {
    const targetY = height - sourceY - 1;
    for (let x = 0; x < width; x += 1) {
      const sourceOffset = (sourceY * width + x) * 4;
      const targetOffset = targetY * rowSize + x * 3;
      pixels[targetOffset] = rgba[sourceOffset + 2];
      pixels[targetOffset + 1] = rgba[sourceOffset + 1];
      pixels[targetOffset + 2] = rgba[sourceOffset];
    }
  }

  return Buffer.concat([header, pixels]);
}

export function assertPng(buffer, expectedWidth, expectedHeight, label) {
  const { width, height } = getPngDimensions(buffer, label);
  if (width !== expectedWidth || height !== expectedHeight) {
    throw new Error(`${label} is ${width}x${height}; expected ${expectedWidth}x${expectedHeight}.`);
  }
}

export function getPngDimensions(buffer, label) {
  if (buffer.length < 24 || !buffer.subarray(0, 8).equals(pngSignature)) {
    throw new Error(`${label} is not a PNG file.`);
  }

  const width = buffer.readUInt32BE(16);
  const height = buffer.readUInt32BE(20);
  return { width, height };
}

export function assertIco(buffer, expectedSizes, label) {
  if (buffer.length < 6 || buffer.readUInt16LE(0) !== 0 || buffer.readUInt16LE(2) !== 1) {
    throw new Error(`${label} is not an ICO file.`);
  }

  const count = buffer.readUInt16LE(4);
  if (count !== expectedSizes.length) {
    throw new Error(`${label} has ${count} images; expected ${expectedSizes.length}.`);
  }

  const actualSizes = [];
  const seenSizes = new Set();
  for (let index = 0; index < count; index += 1) {
    const entryOffset = 6 + index * 16;
    const width = buffer.readUInt8(entryOffset) || 256;
    const height = buffer.readUInt8(entryOffset + 1) || 256;
    const planes = buffer.readUInt16LE(entryOffset + 4);
    const bitsPerPixel = buffer.readUInt16LE(entryOffset + 6);
    const byteLength = buffer.readUInt32LE(entryOffset + 8);
    const imageOffset = buffer.readUInt32LE(entryOffset + 12);
    if (
      width !== height ||
      planes !== 1 ||
      bitsPerPixel !== 32 ||
      imageOffset + byteLength > buffer.length ||
      seenSizes.has(width)
    ) {
      throw new Error(`${label} contains an invalid ICO directory entry.`);
    }
    seenSizes.add(width);
    actualSizes.push(width);

    const frame = buffer.subarray(imageOffset, imageOffset + byteLength);
    if (frame.subarray(0, pngSignature.length).equals(pngSignature)) {
      if (width !== 256) {
        throw new Error(`${label} stores unexpected ${width}px PNG-compressed ICO artwork.`);
      }
      const dimensions = getPngDimensions(frame, `${label} ${width}px frame`);
      const bitDepth = frame.readUInt8(24);
      const colourType = frame.readUInt8(25);
      if (dimensions.width !== width || dimensions.height !== height || bitDepth !== 8 || colourType !== 6) {
        throw new Error(`${label} has an invalid ${width}px RGBA PNG frame.`);
      }
      continue;
    }

    if (width === 256 || frame.length < 40) {
      throw new Error(`${label} must store its 256px frame as a lossless RGBA PNG.`);
    }
    const headerSize = frame.readUInt32LE(0);
    const dibWidth = frame.readInt32LE(4);
    const dibHeight = frame.readInt32LE(8);
    const dibPlanes = frame.readUInt16LE(12);
    const dibBitsPerPixel = frame.readUInt16LE(14);
    const compression = frame.readUInt32LE(16);
    const pixelBytes = width * height * 4;
    if (
      headerSize < 40 ||
      dibWidth !== width ||
      dibHeight !== height * 2 ||
      dibPlanes !== 1 ||
      dibBitsPerPixel !== 32 ||
      compression !== 0 ||
      headerSize + pixelBytes > frame.length
    ) {
      throw new Error(`${label} has an invalid ${width}px 32-bit DIB frame.`);
    }

    let hasTransparentPixel = false;
    let hasVisiblePixel = false;
    for (let offset = headerSize + 3; offset < headerSize + pixelBytes; offset += 4) {
      const alpha = frame[offset];
      hasTransparentPixel ||= alpha === 0;
      hasVisiblePixel ||= alpha > 0;
    }
    if (!hasTransparentPixel || !hasVisiblePixel) {
      throw new Error(`${label} has missing or corrupted alpha in its ${width}px frame.`);
    }
  }

  if (actualSizes.join(",") !== expectedSizes.join(",")) {
    throw new Error(`${label} has sizes ${actualSizes.join(",")}; expected ${expectedSizes.join(",")}.`);
  }
  if (expectedSizes.includes(256) && Math.max(...actualSizes) !== 256) {
    throw new Error(`${label} must stop at a 256px largest frame.`);
  }
}

export function assertBmp24(buffer, expectedWidth, expectedHeight, label) {
  if (buffer.length < 54 || buffer.toString("ascii", 0, 2) !== "BM") {
    throw new Error(`${label} is not a BMP file.`);
  }

  const width = buffer.readInt32LE(18);
  const height = buffer.readInt32LE(22);
  const bitsPerPixel = buffer.readUInt16LE(28);
  if (width !== expectedWidth || height !== expectedHeight || bitsPerPixel !== 24) {
    throw new Error(
      `${label} is ${width}x${height} at ${bitsPerPixel}-bit; expected ${expectedWidth}x${expectedHeight} at 24-bit.`,
    );
  }
}

export function assertOpaque(rgba, label) {
  for (let offset = 3; offset < rgba.length; offset += 4) {
    if (rgba[offset] !== 255) {
      throw new Error(`${label} contains transparent pixels.`);
    }
  }
}

export function assertMaskableSafeZone(rgba, size, background, label) {
  assertRgbaLength(rgba, size, size, label);
  const [red, green, blue] = background;
  const centre = size / 2;
  const safeRadiusSquared = (size * 0.4) ** 2;

  for (let y = 0; y < size; y += 1) {
    for (let x = 0; x < size; x += 1) {
      const offset = (y * size + x) * 4;
      const isBackground =
        rgba[offset] === red && rgba[offset + 1] === green && rgba[offset + 2] === blue;
      if (isBackground) {
        continue;
      }

      const deltaX = x + 0.5 - centre;
      const deltaY = y + 0.5 - centre;
      if (deltaX * deltaX + deltaY * deltaY > safeRadiusSquared) {
        throw new Error(`${label} has artwork outside the maskable safe zone.`);
      }
    }
  }
}

function assertRgbaLength(rgba, width, height, label) {
  const expectedLength = width * height * 4;
  if (rgba.length !== expectedLength) {
    throw new Error(`${label} has ${rgba.length} RGBA bytes; expected ${expectedLength}.`);
  }
}
