//go:build ignore

// Regenerates only the repository-owned Windows icon assets from geometry.
// Run from repository root: go run ./scripts/generate-icon.go
package main

import (
	"bytes"
	"encoding/binary"
	"image"
	"image/color"
	"image/png"
	"math"
	"os"
)

func main() {
	if _, err := os.Stat("wails.json"); err != nil {
		panic("run from repository root")
	}
	sizes := []int{256, 128, 64, 48, 32, 16}
	var blobs [][]byte
	for _, size := range sizes {
		im := image.NewNRGBA(image.Rect(0, 0, size, size))
		for y := 0; y < size; y++ {
			for x := 0; x < size; x++ {
				// Supersampling keeps the same code-native mark legible in the tray.
				var sum [4]int
				for sy := 0; sy < 4; sy++ {
					for sx := 0; sx < 4; sx++ {
						px := (float64(x) + (float64(sx)+.5)/4) * 36 / float64(size)
						py := (float64(y) + (float64(sy)+.5)/4) * 36 / float64(size)
						c := color.NRGBA{}
						if rounded(px, py, 1, 1, 35, 35, 10) {
							c = color.NRGBA{139, 221, 195, 255}
						}
						for _, bar := range [][4]float64{{7.5, 11.5, 10.5, 24.5}, {13.5, 8.5, 16.5, 27.5}, {19.5, 13.5, 22.5, 22.5}, {25.5, 10.5, 28.5, 25.5}} {
							if rounded(px, py, bar[0], bar[1], bar[2], bar[3], 1.5) {
								c = color.NRGBA{16, 35, 37, 255}
							}
						}
						sum[0] += int(c.R) * int(c.A)
						sum[1] += int(c.G) * int(c.A)
						sum[2] += int(c.B) * int(c.A)
						sum[3] += int(c.A)
					}
				}
				if sum[3] > 0 {
					im.SetNRGBA(x, y, color.NRGBA{uint8(sum[0] / sum[3]), uint8(sum[1] / sum[3]), uint8(sum[2] / sum[3]), uint8(sum[3] / 16)})
				}
			}
		}
		var b bytes.Buffer
		if err := png.Encode(&b, im); err != nil {
			panic(err)
		}
		blobs = append(blobs, b.Bytes())
	}
	if err := os.MkdirAll("build/windows", 0755); err != nil {
		panic(err)
	}
	if err := os.WriteFile("build/appicon.png", blobs[0], 0644); err != nil {
		panic(err)
	}
	var ico bytes.Buffer
	for _, v := range []uint16{0, 1, uint16(len(sizes))} {
		if err := binary.Write(&ico, binary.LittleEndian, v); err != nil {
			panic(err)
		}
	}
	offset := uint32(6 + 16*len(sizes))
	for i, size := range sizes {
		ico.Write([]byte{byte(size % 256), byte(size % 256), 0, 0})
		for _, v := range []uint16{1, 32} {
			_ = binary.Write(&ico, binary.LittleEndian, v)
		}
		_ = binary.Write(&ico, binary.LittleEndian, uint32(len(blobs[i])))
		_ = binary.Write(&ico, binary.LittleEndian, offset)
		offset += uint32(len(blobs[i]))
	}
	for _, b := range blobs {
		ico.Write(b)
	}
	if err := os.WriteFile("build/windows/icon.ico", ico.Bytes(), 0644); err != nil {
		panic(err)
	}
}
func rounded(x, y, left, top, right, bottom, radius float64) bool {
	if x < left || x > right || y < top || y > bottom {
		return false
	}
	dx := x - math.Max(left+radius, math.Min(right-radius, x))
	dy := y - math.Max(top+radius, math.Min(bottom-radius, y))
	return dx*dx+dy*dy <= radius*radius
}
