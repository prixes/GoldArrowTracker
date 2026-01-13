function getImageNaturalSize(image) {
    return {
        width: image.naturalWidth,
        height: image.naturalHeight
    };
}

var annotator = {
    init: function (canvas, image, dotNetHelper) {
        this.canvas = canvas;
        this.image = image;
        this.dotNetHelper = dotNetHelper;
        this.ctx = canvas.getContext('2d');
        this.selectedBoxIndex = -1;
        this.isDraggingHandle = false;
        this.activeHandle = null; // 'tl', 'tr', 'bl', 'br', 'center'
        this.isRefining = false;
        this.magnifierSize = 110;
        this.magnifierZoom = 3;
        this.boxes = [];

        // Resize canvas to match image
        this.resizeCanvas();

        // Event listeners
        this.canvas.addEventListener('mousedown', this.handleStart.bind(this));
        this.canvas.addEventListener('mousemove', this.handleMove.bind(this));
        this.canvas.addEventListener('mouseup', this.handleEnd.bind(this));
        this.canvas.addEventListener('mouseleave', this.handleEnd.bind(this));

        // Touch events
        this.canvas.addEventListener('touchstart', (e) => this.handleStart(e));
        this.canvas.addEventListener('touchmove', (e) => this.handleMove(e));
        this.canvas.addEventListener('touchend', (e) => this.handleEnd(e));

        window.addEventListener('resize', this.resizeCanvas.bind(this));
    },

    resizeCanvas: function () {
        this.canvas.width = this.image.clientWidth;
        this.canvas.height = this.image.clientHeight;
        this.redraw();
    },

    handleStart: function (e) {
        if (e.cancelable) e.preventDefault();
        const pos = this.getCanvasCoordinates(e);

        // 1. Check if touching magnifier area for refinement
        if (this.selectedBoxIndex !== -1 && this.isInsideMagnifier(pos)) {
            this.isRefining = true;
            this.lastRefinePos = pos;
            return;
        }

        // 2. Check if touching handles of selected box
        if (this.selectedBoxIndex !== -1) {
            const handle = this.getHandleAt(pos, this.boxes[this.selectedBoxIndex]);
            if (handle) {
                this.isDraggingHandle = true;
                this.activeHandle = handle;
                this.lastPos = pos;
                return;
            }
        }

        // 3. Removed: re-selection of existing boxes is disabled per request.
        // Once deselected, clicking anywhere starts a new box.

        // 4. Otherwise, start drawing a new box and HIDE the current selection/magnifier
        this.isDrawing = true;
        this.startX = pos.x;
        this.startY = pos.y;
        this.selectedBoxIndex = -1;
        this.redraw();
    },

    handleMove: function (e) {
        if (e.cancelable) e.preventDefault();
        const pos = this.getCanvasCoordinates(e);

        if (this.isRefining) {
            this.refineBox(pos);
            return;
        }

        if (this.isDraggingHandle) {
            this.moveHandle(pos);
            return;
        }

        if (this.isDrawing) {
            this.drawPreview(pos);
            return;
        }
    },

    handleEnd: function (e) {
        if (e.cancelable) e.preventDefault();

        if (this.isDrawing) {
            this.stopDrawing(e);
        }

        if (this.isDraggingHandle || this.isRefining) {
            // Update the .NET side with new coordinates
            const box = this.boxes[this.selectedBoxIndex];
            this.dotNetHelper.invokeMethodAsync('UpdateBox',
                this.selectedBoxIndex,
                box.startX, box.startY, box.endX, box.endY);
        }

        this.isDrawing = false;
        this.isDraggingHandle = false;
        this.isRefining = false;
        this.activeHandle = null;
        this.redraw();
    },

    isInsideMagnifier: function (pos) {
        const margin = 10;
        return pos.x >= this.canvas.width - this.magnifierSize - margin &&
            pos.x <= this.canvas.width - margin &&
            pos.y >= margin &&
            pos.y <= this.magnifierSize + margin;
    },

    getHandleAt: function (pos, box) {
        const sx = box.startX * this.canvas.width;
        const sy = box.startY * this.canvas.height;
        const ex = box.endX * this.canvas.width;
        const ey = box.endY * this.canvas.height;
        const size = 30; // Larger hit area for touch

        if (this.dist(pos.x, pos.y, sx, sy) < size) return 'tl';
        if (this.dist(pos.x, pos.y, ex, sy) < size) return 'tr';
        if (this.dist(pos.x, pos.y, sx, ey) < size) return 'bl';
        if (this.dist(pos.x, pos.y, ex, ey) < size) return 'br';
        if (this.isInsideBox(pos, box)) return 'center';
        return null;
    },

    isInsideBox: function (pos, box) {
        const sx = box.startX * this.canvas.width;
        const sy = box.startY * this.canvas.height;
        const ex = box.endX * this.canvas.width;
        const ey = box.endY * this.canvas.height;

        const centerX = (sx + ex) / 2;
        const centerY = (sy + ey) / 2;
        const radius = Math.max(Math.abs(ex - sx), Math.abs(ey - sy)) / 2;

        // Circular hit testing with 10px "forgiveness" buffer for touch
        return this.dist(pos.x, pos.y, centerX, centerY) <= (radius + 10);
    },

    dist: function (x1, y1, x2, y2) {
        return Math.sqrt((x1 - x2) ** 2 + (y1 - y2) ** 2);
    },

    deselect: function () {
        this.selectedBoxIndex = -1;
        this.redraw();
    },

    moveHandle: function (pos) {
        const box = this.boxes[this.selectedBoxIndex];
        const dx = (pos.x - this.lastPos.x) / this.canvas.width;
        const dy = (pos.y - this.lastPos.y) / this.canvas.height;

        if (this.activeHandle === 'center') {
            box.startX += dx; box.endX += dx;
            box.startY += dy; box.endY += dy;
        } else if (this.activeHandle === 'tl') {
            box.startX += dx; box.startY += dy;
        } else if (this.activeHandle === 'tr') {
            box.endX += dx; box.startY += dy;
        } else if (this.activeHandle === 'bl') {
            box.startX += dx; box.endY += dy;
        } else if (this.activeHandle === 'br') {
            box.endX += dx; box.endY += dy;
        }

        this.lastPos = pos;
        this.redraw();
    },

    refineBox: function (pos) {
        const box = this.boxes[this.selectedBoxIndex];
        // High-precision movement: 1/4 speed
        const sensitivity = 0.25;
        const dx = ((pos.x - this.lastRefinePos.x) * sensitivity) / this.canvas.width;
        const dy = ((pos.y - this.lastRefinePos.y) * sensitivity) / this.canvas.height;

        if (this.activeHandle === 'center' || !this.activeHandle) {
            box.startX += dx; box.endX += dx;
            box.startY += dy; box.endY += dy;
        } else if (this.activeHandle === 'tl') {
            box.startX += dx; box.startY += dy;
        } else if (this.activeHandle === 'tr') {
            box.endX += dx; box.startY += dy;
        } else if (this.activeHandle === 'bl') {
            box.startX += dx; box.endY += dy;
        } else if (this.activeHandle === 'br') {
            box.endX += dx; box.endY += dy;
        }

        this.lastRefinePos = pos;
        this.redraw();
    },

    drawPreview: function (pos) {
        this.redraw();
        const startX = this.startX;
        const startY = this.startY;
        const endX = pos.x;
        const endY = pos.y;

        const width = endX - startX;
        const height = endY - startY;
        const centerX = startX + width / 2;
        const centerY = startY + height / 2;
        const radius = Math.max(Math.abs(width), Math.abs(height)) / 2;

        this.ctx.save();
        this.ctx.setLineDash([5, 5]);
        this.ctx.strokeStyle = 'rgba(255, 255, 255, 0.5)';
        this.ctx.strokeRect(startX, startY, width, height);

        this.ctx.setLineDash([]);
        this.ctx.strokeStyle = 'white';
        this.ctx.lineWidth = 4;
        this.ctx.beginPath();
        this.ctx.arc(centerX, centerY, radius, 0, 2 * Math.PI);
        this.ctx.stroke();

        this.ctx.strokeStyle = '#00FFFF';
        this.ctx.lineWidth = 2;
        this.ctx.beginPath();
        this.ctx.arc(centerX, centerY, radius, 0, 2 * Math.PI);
        this.ctx.stroke();

        this.drawCrosshair(startX, startY, 'rgba(255, 255, 255, 0.7)');
        this.drawCrosshair(endX, endY, '#00FFFF');
        this.ctx.restore();

        // Also draw magnifier during initial draw
        const previewBox = {
            startX: (centerX - radius) / this.canvas.width,
            startY: (centerY - radius) / this.canvas.height,
            endX: (centerX + radius) / this.canvas.width,
            endY: (centerY + radius) / this.canvas.height,
            color: '#00FFFF'
        };
        this.drawMagnifier(previewBox, true);
    },

    drawPreviewText: function (x, y, color) {
        // Shared crosshair logic...
    },

    drawCrosshair: function (x, y, color) {
        this.ctx.save();
        this.ctx.strokeStyle = color;
        this.ctx.lineWidth = 1;
        this.ctx.beginPath();
        this.ctx.moveTo(x - 15, y); this.ctx.lineTo(x + 15, y);
        this.ctx.moveTo(x, y - 15); this.ctx.lineTo(x, y + 15);
        this.ctx.stroke();
        this.ctx.beginPath();
        this.ctx.arc(x, y, 3, 0, 2 * Math.PI);
        this.ctx.stroke();
        this.ctx.restore();
    },

    stopDrawing: function (e) {
        const pos = this.getCanvasCoordinates(e);
        const width = pos.x - this.startX;
        const height = pos.y - this.startY;
        const absWidth = Math.abs(width);
        const absHeight = Math.abs(height);

        if (absWidth < 5 && absHeight < 5) return;

        const radius = Math.max(absWidth, absHeight) / 2;
        const centerX = this.startX + width / 2;
        const centerY = this.startY + height / 2;

        this.dotNetHelper.invokeMethodAsync('AddBox',
            (centerX - radius) / this.canvas.width,
            (centerY - radius) / this.canvas.height,
            (centerX + radius) / this.canvas.width,
            (centerY + radius) / this.canvas.height);
    },

    addBox: function (box) {
        this.boxes.push(box);
        this.selectedBoxIndex = this.boxes.length - 1;
        this.activeHandle = 'center'; // Default to center for magnifier
        this.redraw();
    },

    removeLastBox: function () {
        this.boxes.pop();
        this.selectedBoxIndex = this.boxes.length - 1;
        this.redraw();
    },

    getCanvasCoordinates: function (e) {
        const rect = this.canvas.getBoundingClientRect();
        let clientX, clientY;
        if (e.touches && e.touches.length > 0) {
            clientX = e.touches[0].clientX; clientY = e.touches[0].clientY;
        } else if (e.changedTouches && e.changedTouches.length > 0) {
            clientX = e.changedTouches[0].clientX; clientY = e.changedTouches[0].clientY;
        } else {
            clientX = e.clientX; clientY = e.clientY;
        }
        return {
            x: (clientX - rect.left) * (this.canvas.width / rect.width),
            y: (clientY - rect.top) * (this.canvas.height / rect.height)
        };
    },

    redraw: function () {
        this.ctx.clearRect(0, 0, this.canvas.width, this.canvas.height);
        if (this.boxes) {
            this.boxes.forEach((box, index) => {
                const sx = box.startX * this.canvas.width;
                const sy = box.startY * this.canvas.height;
                const ex = box.endX * this.canvas.width;
                const ey = box.endY * this.canvas.height;

                const width = ex - sx;
                const height = ey - sy;
                const radius = Math.max(Math.abs(width), Math.abs(height)) / 2;
                const centerX = sx + width / 2;
                const centerY = sy + height / 2;

                this.ctx.save();
                const isSelected = index === this.selectedBoxIndex;

                // White outline
                this.ctx.strokeStyle = isSelected ? 'cyan' : 'white';
                this.ctx.lineWidth = isSelected ? 4 : 3;
                this.ctx.beginPath();
                this.ctx.arc(centerX, centerY, radius, 0, 2 * Math.PI);
                this.ctx.stroke();

                // Colored inner
                this.ctx.strokeStyle = box.color || 'lime';
                this.ctx.lineWidth = 1.5;
                this.ctx.beginPath();
                this.ctx.arc(centerX, centerY, radius, 0, 2 * Math.PI);
                this.ctx.stroke();

                // Inner rings for alignment
                if (radius > 20) {
                    this.ctx.setLineDash([2, 4]);
                    this.ctx.strokeStyle = 'rgba(255, 255, 255, 0.3)';
                    for (let r = 0.2; r < 1; r += 0.2) {
                        this.ctx.beginPath(); this.ctx.arc(centerX, centerY, radius * r, 0, 2 * Math.PI); this.ctx.stroke();
                    }
                }

                // Label
                this.ctx.font = 'bold 14px sans-serif';
                const labelText = box.label || 'Unknown';
                const metrics = this.ctx.measureText(labelText);
                const labelX = Math.min(sx, ex);
                const labelY = Math.min(sy, ey) > 20 ? Math.min(sy, ey) - 8 : Math.min(sy, ey) + 20;
                this.ctx.fillStyle = 'rgba(0, 0, 0, 0.7)';
                this.ctx.fillRect(labelX - 4, labelY - 14, metrics.width + 8, 18);
                this.ctx.fillStyle = box.color || 'lime';
                this.ctx.fillText(labelText, labelX, labelY);

                // Draw handles if selected
                if (isSelected) {
                    this.ctx.fillStyle = 'white';
                    this.ctx.strokeStyle = 'cyan';
                    this.ctx.lineWidth = 2;
                    const hSize = 8;
                    [[sx, sy], [ex, sy], [sx, ey], [ex, ey]].forEach(p => {
                        this.ctx.fillRect(p[0] - hSize / 2, p[1] - hSize / 2, hSize, hSize);
                        this.ctx.strokeRect(p[0] - hSize / 2, p[1] - hSize / 2, hSize, hSize);
                    });
                }
                this.ctx.restore();
            });
        }

        if (this.selectedBoxIndex !== -1 && !this.isDrawing) {
            this.drawMagnifier();
        }
    },

    drawMagnifier: function (manualBox, isInitial) {
        const box = manualBox || this.boxes[this.selectedBoxIndex];
        if (!box) return;

        // Always center on the object for better alignment with target bullseyes
        const px = (box.startX + box.endX) / 2;
        const py = (box.startY + box.endY) / 2;

        const size = this.magnifierSize;
        const zoom = this.magnifierZoom;
        const margin = 10;
        const magX = this.canvas.width - size - margin;
        const magY = margin;

        this.ctx.save();

        // Draw Magnifier Border & Background
        this.ctx.strokeStyle = 'white';
        this.ctx.lineWidth = 4;
        this.ctx.strokeRect(magX, magY, size, size);
        this.ctx.fillStyle = 'black';
        this.ctx.fillRect(magX, magY, size, size);

        // Clip the magnifier
        this.ctx.beginPath();
        this.ctx.rect(magX, magY, size, size);
        this.ctx.clip();

        // Draw Zoomed Image
        const sSize = size / zoom;
        // Image coordinates (natural size)
        const nSize = getImageNaturalSize(this.image);
        const imgX = px * nSize.width;
        const imgY = py * nSize.height;
        const imgS = (sSize / this.canvas.width) * nSize.width;

        this.ctx.drawImage(this.image,
            imgX - imgS / 2, imgY - imgS / 2, imgS, imgS,
            magX, magY, size, size);

        // Draw preview circle edges in zoom window
        const sx = (box.startX - px) * this.canvas.width * zoom + magX + size / 2;
        const sy = (box.startY - py) * this.canvas.height * zoom + magY + size / 2;
        const ex = (box.endX - px) * this.canvas.width * zoom + magX + size / 2;
        const ey = (box.endY - py) * this.canvas.height * zoom + magY + size / 2;

        const width = ex - sx;
        const height = ey - sy;
        const radius = Math.max(Math.abs(width), Math.abs(height)) / 2;
        const centerX = sx + width / 2;
        const centerY = sy + height / 2;

        this.ctx.strokeStyle = box.color || 'lime';
        this.ctx.lineWidth = 2;
        this.ctx.beginPath();
        this.ctx.arc(centerX, centerY, radius, 0, 2 * Math.PI);
        this.ctx.stroke();

        // Draw Crosshair in Magnifier
        this.ctx.strokeStyle = '#00FFFF';
        this.ctx.setLineDash([]);
        this.ctx.lineWidth = 1;
        this.ctx.beginPath();
        this.ctx.moveTo(magX + size / 2 - 20, magY + size / 2); this.ctx.lineTo(magX + size / 2 + 20, magY + size / 2);
        this.ctx.moveTo(magX + size / 2, magY + size / 2 - 20); this.ctx.lineTo(magX + size / 2, magY + size / 2 + 20);
        this.ctx.stroke();

        // Only show instruction text during "Edit/Refine" mode, not initial draw
        if (!isInitial) {
            this.ctx.fillStyle = 'white';
            this.ctx.font = '7px sans-serif';
            this.ctx.fillText("DRAG HERE FOR PRECISION", magX + 5, magY + size - 5);
        }

        this.ctx.restore();
    },

    clear: function () {
        this.boxes = [];
        this.selectedBoxIndex = -1;
        this.redraw();
    },
    destroy: function () {
        this.canvas.replaceWith(this.canvas.cloneNode(true));
    }
};