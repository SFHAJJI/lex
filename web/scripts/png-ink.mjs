// A minimal PNG reader for the evidence harness: enough to count the ink in a screenshot the
// browser itself took, so a label is proved visible by its pixels rather than by its styles.
//
// Chrome writes 8-bit, non-interlaced RGB or RGBA PNGs; anything else is refused rather than
// guessed at, because a reader that misdecodes would turn "no ink" into a silent pass.

import { inflateSync } from "node:zlib";

const SIGNATURE = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

/** Decode a PNG into `{ width, height, channels, pixels }` with one byte per channel. */
export function decodePng(bytes) {
  const buffer = Buffer.from(bytes);
  if (!buffer.subarray(0, 8).equals(SIGNATURE)) throw new Error("not a PNG");
  let offset = 8;
  let width = 0;
  let height = 0;
  let channels = 0;
  const data = [];
  while (offset < buffer.length) {
    const length = buffer.readUInt32BE(offset);
    const type = buffer.toString("latin1", offset + 4, offset + 8);
    const body = buffer.subarray(offset + 8, offset + 8 + length);
    if (type === "IHDR") {
      width = body.readUInt32BE(0);
      height = body.readUInt32BE(4);
      const depth = body[8];
      const colour = body[9];
      const interlace = body[12];
      if (depth !== 8 || interlace !== 0 || (colour !== 2 && colour !== 6)) {
        throw new Error(`unsupported PNG: depth ${depth}, colour type ${colour}, interlace ${interlace}`);
      }
      channels = colour === 6 ? 4 : 3;
    } else if (type === "IDAT") {
      data.push(body);
    } else if (type === "IEND") {
      break;
    }
    offset += 12 + length;
  }
  const raw = inflateSync(Buffer.concat(data));
  const stride = width * channels;
  const pixels = Buffer.alloc(height * stride);
  for (let y = 0; y < height; y++) {
    const filter = raw[y * (stride + 1)];
    const line = raw.subarray(y * (stride + 1) + 1, (y + 1) * (stride + 1));
    for (let x = 0; x < stride; x++) {
      const left = x >= channels ? pixels[y * stride + x - channels] : 0;
      const up = y > 0 ? pixels[(y - 1) * stride + x] : 0;
      const upLeft = y > 0 && x >= channels ? pixels[(y - 1) * stride + x - channels] : 0;
      let value = line[x];
      if (filter === 1) value += left;
      else if (filter === 2) value += up;
      else if (filter === 3) value += Math.floor((left + up) / 2);
      else if (filter === 4) {
        const estimate = left + up - upLeft;
        const toLeft = Math.abs(estimate - left);
        const toUp = Math.abs(estimate - up);
        const toUpLeft = Math.abs(estimate - upLeft);
        value += toLeft <= toUp && toLeft <= toUpLeft ? left : toUp <= toUpLeft ? up : upLeft;
      } else if (filter !== 0) {
        throw new Error(`unknown PNG filter ${filter}`);
      }
      pixels[y * stride + x] = value & 0xff;
    }
  }
  return { width, height, channels, pixels };
}

/**
 * Ink on paper: how many pixels differ clearly from the most common colour of the image, and what
 * share of the image they are. `threshold` is the largest single-channel difference that still
 * counts as the background, so text drawn at an alpha of 0.01 or in the background's own colour is
 * not ink.
 */
export function inkMeasure({ width, height, channels, pixels }, threshold = 48) {
  const area = width * height;
  if (area === 0) return { pixels: 0, share: 0 };
  const counts = new Map();
  for (let index = 0; index < area; index++) {
    const at = index * channels;
    const key = (pixels[at] << 16) | (pixels[at + 1] << 8) | pixels[at + 2];
    counts.set(key, (counts.get(key) ?? 0) + 1);
  }
  let background = 0;
  let most = -1;
  for (const [key, count] of counts) {
    if (count > most) {
      most = count;
      background = key;
    }
  }
  const [r, g, b] = [(background >> 16) & 0xff, (background >> 8) & 0xff, background & 0xff];
  let ink = 0;
  for (let index = 0; index < area; index++) {
    const at = index * channels;
    const difference = Math.max(
      Math.abs(pixels[at] - r),
      Math.abs(pixels[at + 1] - g),
      Math.abs(pixels[at + 2] - b),
    );
    if (difference > threshold) ink++;
  }
  return { pixels: ink, share: ink / area };
}
