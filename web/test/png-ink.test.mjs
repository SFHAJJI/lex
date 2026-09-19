// The PNG reader the label gate trusts, held on images whose pixels are known.
//
// The gate's verdict on the UNOFFICIAL label comes from a screenshot. A reader that misdecoded a
// filter would turn "no ink" into a pass or a clean label into a failure, so each of the five PNG
// row filters is exercised against a known picture, and the reader refuses what it cannot read.

import assert from "node:assert/strict";
import test from "node:test";
import { deflateSync } from "node:zlib";

import { decodePng, inkMeasure } from "../scripts/png-ink.mjs";

// A 12x6 RGBA picture: white paper, a 3x2 black mark, one grey pixel below the ink threshold.
const WIDTH = 12;
const HEIGHT = 6;
function picture() {
  const pixels = Buffer.alloc(WIDTH * HEIGHT * 4, 0xff);
  for (let y = 2; y < 4; y++) {
    for (let x = 4; x < 7; x++) {
      pixels.set([0, 0, 0, 0xff], (y * WIDTH + x) * 4);
    }
  }
  pixels.set([0xe0, 0xe0, 0xe0, 0xff], (5 * WIDTH + 0) * 4);
  return pixels;
}

// Encode with the given filter on each row, exactly as the PNG specification defines them.
function encode(pixels, filters) {
  const stride = WIDTH * 4;
  const rows = [];
  for (let y = 0; y < HEIGHT; y++) {
    const filter = filters[y % filters.length];
    const row = Buffer.alloc(stride + 1);
    row[0] = filter;
    for (let x = 0; x < stride; x++) {
      const raw = pixels[y * stride + x];
      const left = x >= 4 ? pixels[y * stride + x - 4] : 0;
      const up = y > 0 ? pixels[(y - 1) * stride + x] : 0;
      const upLeft = y > 0 && x >= 4 ? pixels[(y - 1) * stride + x - 4] : 0;
      let predictor = 0;
      if (filter === 1) predictor = left;
      else if (filter === 2) predictor = up;
      else if (filter === 3) predictor = Math.floor((left + up) / 2);
      else if (filter === 4) {
        const estimate = left + up - upLeft;
        const toLeft = Math.abs(estimate - left);
        const toUp = Math.abs(estimate - up);
        const toUpLeft = Math.abs(estimate - upLeft);
        predictor = toLeft <= toUp && toLeft <= toUpLeft ? left : toUp <= toUpLeft ? up : upLeft;
      }
      row[x + 1] = (raw - predictor) & 0xff;
    }
    rows.push(row);
  }
  const chunk = (type, body) => {
    const head = Buffer.alloc(8);
    head.writeUInt32BE(body.length, 0);
    head.write(type, 4, "latin1");
    return Buffer.concat([head, body, Buffer.alloc(4)]); // the reader does not check CRCs
  };
  const header = Buffer.alloc(13);
  header.writeUInt32BE(WIDTH, 0);
  header.writeUInt32BE(HEIGHT, 4);
  header.set([8, 6, 0, 0, 0], 8);
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk("IHDR", header),
    chunk("IDAT", deflateSync(Buffer.concat(rows))),
    chunk("IEND", Buffer.alloc(0)),
  ]);
}

test("every PNG row filter decodes to the picture that was encoded", () => {
  const pixels = picture();
  for (const filters of [[0], [1], [2], [3], [4], [0, 1, 2, 3, 4]]) {
    const image = decodePng(encode(pixels, filters));
    assert.equal(image.width, WIDTH);
    assert.equal(image.height, HEIGHT);
    assert.equal(image.channels, 4);
    assert.ok(image.pixels.equals(pixels), `filters ${filters.join(",")} decoded differently`);
  }
});

test("ink is the pixels that differ clearly from the paper, and a faint grey is not ink", () => {
  const image = decodePng(encode(picture(), [0, 1, 2, 3, 4]));
  const ink = inkMeasure(image);
  assert.equal(ink.pixels, 6, "the 3x2 mark is six pixels of ink; the grey pixel is not ink");
  assert.equal(ink.share, 6 / (WIDTH * HEIGHT));
  const blank = decodePng(encode(Buffer.alloc(WIDTH * HEIGHT * 4, 0xff), [0]));
  assert.deepEqual(inkMeasure(blank), { pixels: 0, share: 0 });
});

test("the reader refuses what it cannot read rather than guessing", () => {
  assert.throws(() => decodePng(Buffer.from("not a png")), /not a PNG/);
  const png = encode(picture(), [0]);
  const grey = Buffer.from(png);
  grey[8 + 8 + 9] = 0; // colour type 0, greyscale
  assert.throws(() => decodePng(grey), /unsupported PNG/);
});
